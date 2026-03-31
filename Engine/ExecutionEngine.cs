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
