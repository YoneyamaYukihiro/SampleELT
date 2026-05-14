using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Engine
{
    /// <summary>
    /// パイプライン実行エンジン。トポロジカルソート順に各ステップを実行し、
    /// 中間データは <see cref="IAsyncEnumerable{T}"/> でストリーミングする。
    ///
    /// メモリ戦略:
    /// - 単入力 + 単出力の連鎖は完全ストリーミング (DB Reader → 行単位変換 → DB 書き込み)。
    /// - ファンアウト (1 ステップ → 複数下流) や多入力ステップ (MergeJoin / TableCompare) の前段は、
    ///   そのステップ実行時に一度だけ <see cref="List{T}"/> へマテリアライズし、複数の下流が同じデータを
    ///   読めるようにする。
    /// - 末端ステップ (出力ノードなど) はエンジンが明示的にドレインして副作用 (DB 書き込み / ファイル出力)
    ///   を発火させる。
    /// - 下流消費がすべて終わったマテリアライズ済みデータは <c>remainingConsumers</c> カウントで検知し、
    ///   即座に参照を切って GC 可能にする。
    /// </summary>
    public class ExecutionEngine
    {
        public async Task ExecuteAsync(Pipeline pipeline, IProgress<string> progress, CancellationToken ct)
        {
            progress.Report("パイプライン実行開始...");

            var steps = pipeline.Steps;
            var connections = pipeline.Connections;

            if (steps.Count == 0)
            {
                progress.Report("ステップがありません。実行をスキップします。");
                return;
            }

            // 事前安全検査
            var issues = PipelineSafetyChecker.Check(pipeline)
                .Where(i => i.Severity == PipelineSafetyChecker.IssueSeverity.Block)
                .ToList();
            if (issues.Count > 0)
            {
                var msg = "実行前検査で以下の問題が検出されました:\n"
                    + PipelineSafetyChecker.Format(issues);
                progress.Report(msg);
                throw new ConnectionResolutionException(msg);
            }

            // 隣接マップ
            var outgoing = new Dictionary<Guid, List<Guid>>();
            var incoming = new Dictionary<Guid, List<Guid>>();
            foreach (var step in steps)
            {
                outgoing[step.Id] = new List<Guid>();
                incoming[step.Id] = new List<Guid>();
            }
            foreach (var conn in connections)
            {
                if (outgoing.ContainsKey(conn.SourceStepId) && incoming.ContainsKey(conn.TargetStepId))
                {
                    outgoing[conn.SourceStepId].Add(conn.TargetStepId);
                    incoming[conn.TargetStepId].Add(conn.SourceStepId);
                }
            }

            // トポロジカルソート
            var sortedIds = new List<Guid>();
            var inDegree = new Dictionary<Guid, int>();
            foreach (var step in steps)
                inDegree[step.Id] = incoming[step.Id].Count;
            var queue = new Queue<Guid>();
            foreach (var step in steps)
            {
                if (inDegree[step.Id] == 0)
                    queue.Enqueue(step.Id);
            }
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                sortedIds.Add(cur);
                foreach (var nxt in outgoing[cur])
                {
                    inDegree[nxt]--;
                    if (inDegree[nxt] == 0)
                        queue.Enqueue(nxt);
                }
            }
            if (sortedIds.Count != steps.Count)
                progress.Report("警告: パイプラインに循環があります。実行できないステップがあります。");

            var stepDict = steps.ToDictionary(s => s.Id);

            // 中間データの保持先:
            //   stepStream      : ストリーミングのまま下流に渡せる場合 (単下流 + 多入力ではない)
            //   stepMaterialized: ファンアウト / 多入力下流があり List 化が必要な場合
            var stepStream = new Dictionary<Guid, IAsyncEnumerable<Dictionary<string, object?>>>();
            var stepMaterialized = new Dictionary<Guid, List<Dictionary<string, object?>>>();

            // 各ノードの残消費カウント (これが 0 になったら参照を切って GC へ)
            var remainingConsumers = new Dictionary<Guid, int>();
            foreach (var stepId in sortedIds)
                remainingConsumers[stepId] = outgoing[stepId].Count;

            try
            {
                foreach (var stepId in sortedIds)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!stepDict.TryGetValue(stepId, out var step))
                        continue;

                    var preds = incoming[stepId];
                    var outDeg = outgoing[stepId].Count;
                    // ファンアウトのときのみ前段を List 化する (多入力ステップでも各前段を 1 回しか読まないので
                    // ストリームのまま渡せばよい)
                    var needsMaterialize = outDeg > 1;

                    // 入力ストリームを構築
                    IAsyncEnumerable<Dictionary<string, object?>> inputStream;
                    if (preds.Count == 0)
                    {
                        inputStream = EmptyAsync();
                        step.AllInputStreams = new List<IAsyncEnumerable<Dictionary<string, object?>>>();
                    }
                    else if (preds.Count == 1)
                    {
                        inputStream = GetInputStream(preds[0], stepStream, stepMaterialized);
                        // 単入力では AllInputStreams は空 (使う step が無い)
                        step.AllInputStreams = new List<IAsyncEnumerable<Dictionary<string, object?>>>();
                    }
                    else
                    {
                        // 多入力: 各前段のストリームをそのまま AllInputStreams にセット。
                        // 多入力ステップは各ストリームを 1 回だけ列挙する責務がある。
                        var predStreams = preds
                            .Select(pid => GetInputStream(pid, stepStream, stepMaterialized))
                            .ToList();
                        step.AllInputStreams = predStreams;
                        // input 引数は連結したものを渡す (多入力ステップは通常無視するが後方互換のため)
                        inputStream = ConcatStreamsAsync(predStreams);
                    }

                    // 接続ステップは「どの DB に書くか」を実行前にログへ
                    if (step.Settings.TryGetValue("ConnectionId", out var cidObj)
                        && cidObj != null
                        && Guid.TryParse(cidObj.ToString(), out var cidGuid))
                    {
                        var conn = Models.Stores.IConnectionStore.Default.GetById(cidGuid);
                        progress.Report($"[{step.Name}] 接続: {ConnectionSafety.DescribeConnection(conn)}");
                    }
                    progress.Report($"[{step.Name}] 実行中...");

                    // ステップ出力 (ストリーミング + プログレス・例外ラップ済み)
                    var rawOutput = SafeExecuteStreaming(step, inputStream, progress, ct);
                    var wrapped = WrapWithProgress(step, rawOutput, progress, ct);

                    try
                    {
                        if (needsMaterialize)
                        {
                            // ファンアウト or 多入力の前段 → 一度 List 化して再利用可能に
                            var list = new List<Dictionary<string, object?>>();
                            await foreach (var row in wrapped.WithCancellation(ct).ConfigureAwait(false))
                                list.Add(row);
                            stepMaterialized[stepId] = list;
                        }
                        else if (outDeg == 0)
                        {
                            // 末端ステップ → 副作用 (DB 書き込み / ファイル出力) を発火させるためドレイン
                            await foreach (var _ in wrapped.WithCancellation(ct).ConfigureAwait(false))
                            {
                                // no-op: ステップ内部で書き込みなどが実行される
                            }
                        }
                        else
                        {
                            // 単下流 → ストリームのまま下流に渡す (実体化しない)
                            stepStream[stepId] = wrapped;
                        }
                    }
                    finally
                    {
                        // ステップ完了直後に AllInputStreams を切り離して GC 可能に
                        step.AllInputStreams = new List<IAsyncEnumerable<Dictionary<string, object?>>>();
                    }

                    // 前段ノードのうち、もう全消費が終わったものは参照を切る
                    foreach (var predId in preds)
                    {
                        if (!remainingConsumers.TryGetValue(predId, out var rem)) continue;
                        rem--;
                        remainingConsumers[predId] = rem;
                        if (rem <= 0)
                        {
                            stepStream.Remove(predId);
                            stepMaterialized.Remove(predId);
                        }
                    }
                }

                progress.Report("パイプライン実行完了");
            }
            finally
            {
                foreach (var step in steps)
                    step.AllInputStreams = new List<IAsyncEnumerable<Dictionary<string, object?>>>();
                stepStream.Clear();
                stepMaterialized.Clear();
            }
        }

        /// <summary>前段ノードから入力ストリームを取得する (実体化済みなら List から、未実体ならストリームを直接)。</summary>
        private static IAsyncEnumerable<Dictionary<string, object?>> GetInputStream(
            Guid predId,
            Dictionary<Guid, IAsyncEnumerable<Dictionary<string, object?>>> stepStream,
            Dictionary<Guid, List<Dictionary<string, object?>>> stepMaterialized)
        {
            if (stepMaterialized.TryGetValue(predId, out var list))
                return ListAsAsync(list);
            if (stepStream.TryGetValue(predId, out var stream))
                return stream;
            return EmptyAsync();
        }

        /// <summary>
        /// ステップの ExecuteStreamingAsync を呼ぶ際の例外保護。
        /// 呼び出し自体で投げられる例外 (引数バリデーションなど) は捕捉してログに出し、空ストリームを返す。
        /// 列挙中に投げられる例外は <see cref="WrapWithProgress"/> 側で処理する。
        /// </summary>
        private static IAsyncEnumerable<Dictionary<string, object?>> SafeExecuteStreaming(
            StepBase step,
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            CancellationToken ct)
        {
            try
            {
                return step.ExecuteStreamingAsync(input, progress, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ConnectionResolutionException)
            {
                progress.Report($"[{step.Name}] 接続解決失敗のためパイプラインを中断します");
                throw;
            }
            catch (Exception ex)
            {
                progress.Report($"[{step.Name}] エラー: {ex.Message}");
                return EmptyAsync();
            }
        }

        /// <summary>
        /// ステップ出力の列挙を進捗ログでラップする。
        /// - 行数をカウントし、列挙終了時に「[Step] 完了 (N行)」を出す
        /// - 列挙中の例外は捕捉してログ出力 (キャンセル / 接続解決失敗は再スローでパイプライン中断)
        /// </summary>
        private static async IAsyncEnumerable<Dictionary<string, object?>> WrapWithProgress(
            StepBase step,
            IAsyncEnumerable<Dictionary<string, object?>> source,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await using var en = source.GetAsyncEnumerator(ct);
            int count = 0;
            while (true)
            {
                bool hasNext = false;
                Dictionary<string, object?>? next = null;
                Exception? iterError = null;
                try
                {
                    hasNext = await en.MoveNextAsync().ConfigureAwait(false);
                    if (hasNext) next = en.Current;
                }
                catch (OperationCanceledException)
                {
                    progress.Report($"[{step.Name}] キャンセルされました");
                    throw;
                }
                catch (ConnectionResolutionException)
                {
                    progress.Report($"[{step.Name}] 接続解決失敗のためパイプラインを中断します");
                    throw;
                }
                catch (Exception ex)
                {
                    iterError = ex;
                }

                if (iterError != null)
                {
                    progress.Report($"[{step.Name}] エラー: {iterError.Message}");
                    break;
                }
                if (!hasNext) break;

                count++;
                yield return next!;
            }
            progress.Report($"[{step.Name}] 完了 ({count}行)");
        }

        private static async IAsyncEnumerable<Dictionary<string, object?>> EmptyAsync()
        {
            await Task.CompletedTask;
            yield break;
        }

        private static async IAsyncEnumerable<Dictionary<string, object?>> ListAsAsync(
            List<Dictionary<string, object?>> list)
        {
            foreach (var row in list)
                yield return row;
            await Task.CompletedTask;
        }

        private static async IAsyncEnumerable<Dictionary<string, object?>> ConcatStreamsAsync(
            List<IAsyncEnumerable<Dictionary<string, object?>>> streams)
        {
            foreach (var stream in streams)
                await foreach (var row in stream.ConfigureAwait(false))
                    yield return row;
        }
    }
}
