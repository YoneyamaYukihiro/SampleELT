using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Engine;
using SampleELT.Models;
using Xunit;

namespace SampleELT.Tests.Engine
{
    /// <summary>
    /// JobExecutor テスト。
    /// 一時ディレクトリに最小限のパイプライン JSON を書き出して実行する。
    /// </summary>
    public class JobExecutorTests : IDisposable
    {
        private readonly string _tempDir;

        public JobExecutorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "JobExecutorTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        /// <summary>GenerateRows (1行) のみを持つ最小パイプラインを JSON ファイルとして書き出す。</summary>
        private string CreatePipelineFile(string name, string fileName = "")
        {
            var stepId = Guid.NewGuid();
            var pipeline = new
            {
                Name = name,
                Steps = new[]
                {
                    new
                    {
                        Id = stepId,
                        Name = "Gen",
                        StepType = "GenerateRows",
                        CanvasX = 0.0,
                        CanvasY = 0.0,
                        Settings = new Dictionary<string, string?>
                        {
                            ["Fields"] = "VALUE=1",
                            ["RowCount"] = "1"
                        }
                    }
                },
                Connections = Array.Empty<object>()
            };

            var filePath = Path.Combine(_tempDir, string.IsNullOrEmpty(fileName) ? $"{name}.json" : fileName);
            File.WriteAllText(filePath, JsonSerializer.Serialize(pipeline, new JsonSerializerOptions { WriteIndented = true }));
            return filePath;
        }

        [Fact]
        public async Task EmptyJob_CompletesWithoutError()
        {
            var job = new Job { Name = "Empty Job" };
            var log = new List<string>();
            var executor = new JobExecutor();
            await executor.ExecuteAsync(job, new SyncProgress(log), CancellationToken.None);
            Assert.Contains(log, m => m.Contains("Empty Job") && m.Contains("完了"));
        }

        [Fact]
        public async Task SinglePipeline_ExecutesSuccessfully()
        {
            var pipelinePath = CreatePipelineFile("Pipeline1");
            var job = new Job
            {
                Name = "Single Step Job",
                Steps = new List<JobStep>
                {
                    new() { Order = 1, Name = "Step1", PipelineFilePath = pipelinePath }
                }
            };

            var log = new List<string>();
            var executor = new JobExecutor();
            await executor.ExecuteAsync(job, new SyncProgress(log), CancellationToken.None);

            Assert.Contains(log, m => m.Contains("完了"));
        }

        [Fact]
        public async Task MultiplePipelines_ExecutesInOrder()
        {
            var p1 = CreatePipelineFile("PipeA", "pA.json");
            var p2 = CreatePipelineFile("PipeB", "pB.json");
            var executionOrder = new List<int>();

            var job = new Job
            {
                Name = "Multi Job",
                Steps = new List<JobStep>
                {
                    new() { Order = 2, Name = "Step2", PipelineFilePath = p2 },
                    new() { Order = 1, Name = "Step1", PipelineFilePath = p1 },
                }
            };

            var log = new List<string>();
            await new JobExecutor().ExecuteAsync(job, new SyncProgress(log), CancellationToken.None);

            // Step1 (Order=1) は Step2 (Order=2) より前にログに出るはず
            var step1Idx = log.FindIndex(m => m.Contains("Step1"));
            var step2Idx = log.FindIndex(m => m.Contains("Step2"));
            Assert.True(step1Idx < step2Idx, "Order=1 の Step1 が Order=2 の Step2 より先に実行されること");
        }

        [Fact]
        public async Task PipelineError_WithContinueOnError_Continues()
        {
            var badPath = Path.Combine(_tempDir, "nonexistent.json");
            var goodPath = CreatePipelineFile("GoodPipeline", "good.json");

            var job = new Job
            {
                Name = "ContinueOnError Job",
                Steps = new List<JobStep>
                {
                    new() { Order = 1, Name = "BadStep", PipelineFilePath = badPath, ContinueOnError = true },
                    new() { Order = 2, Name = "GoodStep", PipelineFilePath = goodPath, ContinueOnError = false },
                }
            };

            var log = new List<string>();
            await new JobExecutor().ExecuteAsync(job, new SyncProgress(log), CancellationToken.None);

            // エラーを無視して続行し、GoodStep も完了することを確認
            Assert.Contains(log, m => m.Contains("GoodStep"));
        }

        [Fact]
        public async Task PipelineError_WithoutContinueOnError_Throws()
        {
            var badPath = Path.Combine(_tempDir, "nonexistent.json");
            var job = new Job
            {
                Name = "Abort Job",
                Steps = new List<JobStep>
                {
                    new() { Order = 1, Name = "BadStep", PipelineFilePath = badPath, ContinueOnError = false },
                }
            };

            var log = new List<string>();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new JobExecutor().ExecuteAsync(job, new SyncProgress(log), CancellationToken.None));
        }

        [Fact]
        public async Task Cancellation_ThrowsOperationCanceled()
        {
            var pipelinePath = CreatePipelineFile("CancelPipeline");
            var job = new Job
            {
                Name = "Cancel Job",
                Steps = new List<JobStep>
                {
                    new() { Order = 1, Name = "Step1", PipelineFilePath = pipelinePath }
                }
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => new JobExecutor().ExecuteAsync(job, new SyncProgress(), cts.Token));
        }

        [Fact]
        public async Task ProgressReports_ContainJobName()
        {
            var job = new Job { Name = "ReportTest Job" };
            var log = new List<string>();
            await new JobExecutor().ExecuteAsync(job, new SyncProgress(log), CancellationToken.None);
            Assert.Contains(log, m => m.Contains("ReportTest Job"));
        }
    }
}
