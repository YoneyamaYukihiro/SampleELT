using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Models;

namespace BreezeFlow.Engine
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
    ///
    /// 多ポート (分岐) ステップ:
    /// - <see cref="StepBase.OutputPorts"/> が複数ポートを宣言するステップ (Filter / Switch 等) は
    ///   <see cref="StepBase.ExecuteRoutedAsync"/> 経由でポート別の行を出力する。
    /// - 分岐ステップの出力は常に per-branch でマテリアライズ (List 化) する。中間データのサイズは
    ///   行数依存だが、Filter / Switch は通常 row-by-row 判定するだけのステップなので
    ///   メモリピークの主因にはなりにくい。
    /// - 下流接続は <see cref="PipelineConnection.SourceBranchKey"/> で「どのポートから出る行を読むか」
    ///   を選択する。
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

            // 隣接マップ (BranchKey 付き)
            var outgoing = new Dictionary<Guid, List<OutEdge>>();
            var incoming = new Dictionary<Guid, List<InEdge>>();
            foreach (var step in steps)
            {
                outgoing[step.Id] = new List<OutEdge>();
                incoming[step.Id] = new List<InEdge>();
            }
            foreach (var conn in connections)
            {
                if (outgoing.ContainsKey(conn.SourceStepId) && incoming.ContainsKey(conn.TargetStepId))
                {
                    var bk = conn.SourceBranchKey ?? string.Empty;
                    outgoing[conn.SourceStepId].Add(new OutEdge(conn.TargetStepId, bk));
                    incoming[conn.TargetStepId].Add(new InEdge(conn.SourceStepId, bk));
                }
            }

            // トポロジカルソート (BranchKey 無視で純粋に DAG として処理)
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
                    inDegree[nxt.Target]--;
                    if (inDegree[nxt.Target] == 0)
                        queue.Enqueue(nxt.Target);
                }
            }
            if (sortedIds.Count != steps.Count)
                progress.Report("警告: パイプラインに循環があります。実行できないステップがあります。");

            var stepDict = steps.ToDictionary(s => s.Id);

            // 中間データの保持先 (Key = (StepId, BranchKey)):
            //   stepStream      : ストリーミングのまま下流に渡せる場合 (単ポート + 単下流)
            //   stepMaterialized: ファンアウト / 多入力下流 / 多ポート分岐の場合
            var stepStream = new Dictionary<(Guid, string), IAsyncEnumerable<Dictionary<string, object?>>>();
            var stepMaterialized = new Dictionary<(Guid, string), List<Dictionary<string, object?>>>();

            // 各ノードの残消費カウント (これが 0 になったら全ブランチを GC へ)
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
                    var outs = outgoing[stepId];
                    var outDeg = outs.Count;

                    // 入力ストリーム構築 (preds の各 InEdge は (SourceStepId, BranchKey) を持つ)
                    IAsyncEnumerable<Dictionary<string, object?>> inputStream;
                    if (preds.Count == 0)
                    {
                        inputStream = EmptyAsync();
                        step.AllInputStreams = new List<IAsyncEnumerable<Dictionary<string, object?>>>();
                    }
                    else if (preds.Count == 1)
                    {
                        inputStream = GetInputStream(preds[0], stepStream, stepMaterialized);
                        step.AllInputStreams = new List<IAsyncEnumerable<Dictionary<string, object?>>>();
                    }
                    else
                    {
                        var predStreams = preds
                            .Select(p => GetInputStream(p, stepStream, stepMaterialized))
                            .ToList();
                        step.AllInputStreams = predStreams;
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

                    var ports = step.OutputPorts;
                    bool isMultiPort = IsMultiPort(ports);

                    if (isMultiPort)
                    {
                        // 多ポートステップ: ExecuteRoutedAsync を呼び、ブランチごとに List 化
                        var routed = SafeExecuteRouted(step, inputStream, progress, ct);
                        var wrapped = WrapRoutedWithProgress(step, routed, progress, ct);

                        var perBranch = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);
                        await foreach (var rr in wrapped.WithCancellation(ct).ConfigureAwait(false))
                        {
                            if (!perBranch.TryGetValue(rr.BranchKey, out var list))
                            {
                                list = new List<Dictionary<string, object?>>();
                                perBranch[rr.BranchKey] = list;
                            }
                            list.Add(rr.Row);
                        }
                        // 宣言済みポートで行が来なかったものも空 List で登録 (下流から空ストリームとして見えるように)
                        foreach (var p in ports)
                        {
                            if (!perBranch.ContainsKey(p.Key))
                                perBranch[p.Key] = new List<Dictionary<string, object?>>();
                        }
                        foreach (var kv in perBranch)
                            stepMaterialized[(stepId, kv.Key)] = kv.Value;
                    }
                    else
                    {
                        // 単ポートステップ: 従来パス (ExecuteStreamingAsync)
                        var rawOutput = SafeExecuteStreaming(step, inputStream, progress, ct);
                        var wrapped = WrapWithProgress(step, rawOutput, progress, ct);

                        bool needsMaterialize = outDeg > 1;
                        if (needsMaterialize)
                        {
                            var list = new List<Dictionary<string, object?>>();
                            await foreach (var row in wrapped.WithCancellation(ct).ConfigureAwait(false))
                                list.Add(row);
                            stepMaterialized[(stepId, string.Empty)] = list;
                        }
                        else if (outDeg == 0)
                        {
                            // 末端: ドレインして副作用を発火
                            await foreach (var _ in wrapped.WithCancellation(ct).ConfigureAwait(false))
                            {
                            }
                        }
                        else
                        {
                            // 単下流: ストリームのまま渡す (遅延列挙)。
                            // step.AllInputStreams はここで切ってはいけない。WrapWithProgress の finally で
                            // 列挙完了時にクリアされる。
                            stepStream[(stepId, string.Empty)] = wrapped;
                        }
                    }

                    // 前段ノードの全消費が終わったら参照を切る
                    foreach (var pred in preds)
                    {
                        if (!remainingConsumers.TryGetValue(pred.Source, out var rem)) continue;
                        rem--;
                        remainingConsumers[pred.Source] = rem;
                        if (rem <= 0)
                        {
                            var streamKeys = stepStream.Keys.Where(k => k.Item1 == pred.Source).ToList();
                            foreach (var k in streamKeys) stepStream.Remove(k);
                            var matKeys = stepMaterialized.Keys.Where(k => k.Item1 == pred.Source).ToList();
                            foreach (var k in matKeys) stepMaterialized.Remove(k);
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

        /// <summary>ステップが多ポート (分岐) として振る舞うかどうか。</summary>
        private static bool IsMultiPort(IReadOnlyList<OutputPort> ports)
        {
            if (ports.Count > 1) return true;
            if (ports.Count == 1 && !string.IsNullOrEmpty(ports[0].Key)) return true;
            return false;
        }

        /// <summary>前段ノードから入力ストリームを取得する。</summary>
        private static IAsyncEnumerable<Dictionary<string, object?>> GetInputStream(
            InEdge edge,
            Dictionary<(Guid, string), IAsyncEnumerable<Dictionary<string, object?>>> stream,
            Dictionary<(Guid, string), List<Dictionary<string, object?>>> materialized)
        {
            var key = (edge.Source, edge.Branch);
            if (materialized.TryGetValue(key, out var list))
                return ListAsAsync(list);
            if (stream.TryGetValue(key, out var s))
                return s;

            // フォールバック: BranchKey が空でない場合、デフォルトポート ("") から取る。
            // (旧 JSON でブランチキー未指定の場合や、後から source がポート構成を変えた場合に発生し得る)
            if (!string.IsNullOrEmpty(edge.Branch))
            {
                var fallback = (edge.Source, string.Empty);
                if (materialized.TryGetValue(fallback, out var fl)) return ListAsAsync(fl);
                if (stream.TryGetValue(fallback, out var fs)) return fs;
            }
            return EmptyAsync();
        }

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

        private static IAsyncEnumerable<RoutedRow> SafeExecuteRouted(
            StepBase step,
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            CancellationToken ct)
        {
            try
            {
                return step.ExecuteRoutedAsync(input, progress, ct);
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
                return EmptyRoutedAsync();
            }
        }

        private static async IAsyncEnumerable<Dictionary<string, object?>> WrapWithProgress(
            StepBase step,
            IAsyncEnumerable<Dictionary<string, object?>> source,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            int count = 0;
            try
            {
                await using var en = source.GetAsyncEnumerator(ct);
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
            finally
            {
                step.AllInputStreams = new List<IAsyncEnumerable<Dictionary<string, object?>>>();
            }
        }

        private static async IAsyncEnumerable<RoutedRow> WrapRoutedWithProgress(
            StepBase step,
            IAsyncEnumerable<RoutedRow> source,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            int count = 0;
            try
            {
                await using var en = source.GetAsyncEnumerator(ct);
                while (true)
                {
                    bool hasNext = false;
                    RoutedRow next = default;
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
                    yield return next;
                }
                progress.Report($"[{step.Name}] 完了 ({count}行)");
            }
            finally
            {
                step.AllInputStreams = new List<IAsyncEnumerable<Dictionary<string, object?>>>();
            }
        }

        private static async IAsyncEnumerable<Dictionary<string, object?>> EmptyAsync()
        {
            await Task.CompletedTask;
            yield break;
        }

        private static async IAsyncEnumerable<RoutedRow> EmptyRoutedAsync()
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

        private readonly record struct OutEdge(Guid Target, string Branch);
        private readonly record struct InEdge(Guid Source, string Branch);
    }
}
