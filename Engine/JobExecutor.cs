using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Models;
using BreezeFlow.Services;

namespace BreezeFlow.Engine
{
    /// <summary>
    /// 複数のパイプラインを順次実行するジョブエグゼキュータ。
    /// <see cref="RunHistoryStore"/> が初期化されている場合、
    /// ジョブ内の各パイプラインを 1 件ずつ run 履歴へ記録する。
    /// </summary>
    public class JobExecutor
    {
        public async Task ExecuteAsync(
            Job job,
            IProgress<string> progress,
            CancellationToken ct,
            string trigger = "job")
        {
            progress.Report($"ジョブ「{job.Name}」開始（{job.Steps.Count} パイプライン）");

            var steps = job.Steps.OrderBy(s => s.Order).ToList();
            var store = RunHistoryStore.Instance;

            for (int i = 0; i < steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var jobStep = steps[i];
                var fileName = System.IO.Path.GetFileNameWithoutExtension(jobStep.PipelineFilePath);
                var stepLabel = string.IsNullOrWhiteSpace(jobStep.Name)
                    ? fileName
                    : $"{fileName} « {jobStep.Name} »";

                progress.Report($"[{i + 1}/{steps.Count}] パイプライン実行: {stepLabel}");

                long runId = -1;
                RunHistoryProgressWriter? historyWriter = null;
                Pipeline? pipeline = null;
                RunStatus finalStatus = RunStatus.Failed;
                string? finalError = null;

                try
                {
                    pipeline = PipelineLoader.LoadFromFile(jobStep.PipelineFilePath);

                    IProgress<string> stepProgress = progress;
                    if (store != null)
                    {
                        runId = store.BeginRun(new RunRecord
                        {
                            PipelinePath = jobStep.PipelineFilePath,
                            PipelineName = pipeline.Name,
                            JobPath = job.FilePath,
                            JobName = job.Name,
                            Trigger = trigger,
                            StartedAt = DateTime.Now
                        });
                        historyWriter = new RunHistoryProgressWriter(store, runId, progress);
                        stepProgress = historyWriter;
                    }

                    var engine = new ExecutionEngine();
                    await engine.ExecuteAsync(pipeline, stepProgress, ct);

                    finalStatus = RunStatus.Success;
                    progress.Report($"[{i + 1}/{steps.Count}] 完了: {stepLabel}");
                }
                catch (OperationCanceledException)
                {
                    finalStatus = RunStatus.Cancelled;
                    finalError = "キャンセル";
                    progress.Report($"ジョブ「{job.Name}」がキャンセルされました（ステップ {i + 1}）");
                    historyWriter?.FinishUnclosedSteps(RunStatus.Cancelled, finalError);
                    if (runId > 0 && store != null)
                        store.EndRun(runId, finalStatus, finalError, historyWriter?.LastReportedRowCount);
                    throw;
                }
                catch (Exception ex)
                {
                    finalStatus = RunStatus.Failed;
                    finalError = ex.Message;
                    var msg = $"[{i + 1}/{steps.Count}] エラー: {stepLabel} — {ex.Message}";
                    progress.Report(msg);
                    historyWriter?.FinishUnclosedSteps(RunStatus.Failed, finalError);
                    if (runId > 0 && store != null)
                        store.EndRun(runId, finalStatus, finalError, historyWriter?.LastReportedRowCount);

                    if (!jobStep.ContinueOnError)
                    {
                        progress.Report($"ジョブ「{job.Name}」を中断します（ContinueOnError = false）");
                        throw new InvalidOperationException(msg, ex);
                    }
                    progress.Report("エラーを無視して続行します...");
                    continue;
                }

                if (runId > 0 && store != null)
                    store.EndRun(runId, finalStatus, finalError, historyWriter?.LastReportedRowCount);
            }

            progress.Report($"ジョブ「{job.Name}」完了");
        }
    }
}
