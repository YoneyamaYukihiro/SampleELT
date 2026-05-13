using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Engine
{
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

            // 事前安全検査: 未解決接続 / Read-only 接続への書き込みは即時ブロック
            // (Production 書き込みの確認は対話可能な UI 側で処理済み)
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

            // Build adjacency maps
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

            // Topological sort (Kahn's algorithm)
            var sortedIds = new List<Guid>();
            var inDegree = new Dictionary<Guid, int>();
            foreach (var step in steps)
            {
                inDegree[step.Id] = incoming[step.Id].Count;
            }

            var queue = new Queue<Guid>();
            foreach (var step in steps)
            {
                if (inDegree[step.Id] == 0)
                    queue.Enqueue(step.Id);
            }

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                sortedIds.Add(currentId);

                foreach (var nextId in outgoing[currentId])
                {
                    inDegree[nextId]--;
                    if (inDegree[nextId] == 0)
                        queue.Enqueue(nextId);
                }
            }

            if (sortedIds.Count != steps.Count)
            {
                progress.Report("警告: パイプラインに循環があります。実行できないステップがあります。");
            }

            // Track output data per step
            var stepOutputs = new Dictionary<Guid, List<Dictionary<string, object?>>>();

            // Execute steps in topological order
            var stepDict = steps.ToDictionary(s => s.Id);

            foreach (var stepId in sortedIds)
            {
                ct.ThrowIfCancellationRequested();

                if (!stepDict.TryGetValue(stepId, out var step))
                    continue;

                // Gather input: use output of all predecessors (merge if multiple)
                var inputData = new List<Dictionary<string, object?>>();
                var predecessors = incoming[stepId];
                if (predecessors.Count == 0)
                {
                    // Root step: empty input
                    inputData = new List<Dictionary<string, object?>>();
                }
                else if (predecessors.Count == 1)
                {
                    var predId = predecessors[0];
                    if (stepOutputs.TryGetValue(predId, out var predOutput))
                        inputData = predOutput;
                }
                else
                {
                    // Merge multiple inputs
                    foreach (var predId in predecessors)
                    {
                        if (stepOutputs.TryGetValue(predId, out var predOutput))
                            inputData.AddRange(predOutput);
                    }
                }

                // Provide all input streams for multi-input steps (e.g. MergeJoin)
                step.AllInputStreams = predecessors
                    .Select(pid => stepOutputs.TryGetValue(pid, out var po) ? po : new List<Dictionary<string, object?>>())
                    .ToList();

                // 接続ステップは「どの DB に書くか」をログに明示し、誤実行を後から追跡可能にする
                if (step.Settings.TryGetValue("ConnectionId", out var cidObj)
                    && cidObj != null
                    && Guid.TryParse(cidObj.ToString(), out var cidGuid))
                {
                    var conn = Models.Stores.IConnectionStore.Default.GetById(cidGuid);
                    progress.Report($"[{step.Name}] 接続: {ConnectionSafety.DescribeConnection(conn)}");
                }

                progress.Report($"[{step.Name}] 実行中...");

                try
                {
                    var output = await step.ExecuteAsync(inputData, progress, ct);
                    stepOutputs[stepId] = output;
                    progress.Report($"[{step.Name}] 完了 ({output.Count}行)");
                }
                catch (OperationCanceledException)
                {
                    progress.Report($"[{step.Name}] キャンセルされました");
                    throw;
                }
                catch (ConnectionResolutionException)
                {
                    // 接続未解決は安全上即時中断 (続行すると別 DB を叩く危険)
                    progress.Report($"[{step.Name}] 接続解決失敗のためパイプラインを中断します");
                    throw;
                }
                catch (Exception ex)
                {
                    progress.Report($"[{step.Name}] エラー: {ex.Message}");
                    // Log and continue with empty output for this step
                    stepOutputs[stepId] = new List<Dictionary<string, object?>>();
                }
            }

            progress.Report("パイプライン実行完了");
        }
    }
}
