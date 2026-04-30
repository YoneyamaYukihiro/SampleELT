using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Engine
{
    /// <summary>
    /// 複数のパイプラインを順次実行するジョブエグゼキュータ。
    /// </summary>
    public class JobExecutor
    {
        public async Task ExecuteAsync(Job job, IProgress<string> progress, CancellationToken ct)
        {
            progress.Report($"ジョブ「{job.Name}」開始（{job.Steps.Count} パイプライン）");

            var steps = job.Steps.OrderBy(s => s.Order).ToList();

            for (int i = 0; i < steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var jobStep = steps[i];
                var stepLabel = string.IsNullOrWhiteSpace(jobStep.Name)
                    ? System.IO.Path.GetFileNameWithoutExtension(jobStep.PipelineFilePath)
                    : jobStep.Name;

                progress.Report($"[{i + 1}/{steps.Count}] パイプライン実行: {stepLabel}");

                try
                {
                    var pipeline = PipelineLoader.LoadFromFile(jobStep.PipelineFilePath);
                    var engine = new ExecutionEngine();
                    await engine.ExecuteAsync(pipeline, progress, ct);
                    progress.Report($"[{i + 1}/{steps.Count}] 完了: {stepLabel}");
                }
                catch (OperationCanceledException)
                {
                    progress.Report($"ジョブ「{job.Name}」がキャンセルされました（ステップ {i + 1}）");
                    throw;
                }
                catch (Exception ex)
                {
                    var msg = $"[{i + 1}/{steps.Count}] エラー: {stepLabel} — {ex.Message}";
                    progress.Report(msg);

                    if (!jobStep.ContinueOnError)
                    {
                        progress.Report($"ジョブ「{job.Name}」を中断します（ContinueOnError = false）");
                        throw new InvalidOperationException(msg, ex);
                    }
                    progress.Report("エラーを無視して続行します...");
                }
            }

            progress.Report($"ジョブ「{job.Name}」完了");
        }
    }
}
