using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Engine;
using SampleELT.Models;
using SampleELT.Steps;
using Xunit;

namespace SampleELT.Tests.Engine
{
    public class ExecutionEngineTests
    {
        private static async Task<List<string>> Execute(
            Pipeline pipeline, CancellationToken ct = default)
        {
            var log = new List<string>();
            await new ExecutionEngine().ExecuteAsync(pipeline, new SyncProgress(log), ct);
            return log;
        }

        [Fact]
        public async Task EmptyPipeline_CompletesWithoutError()
        {
            var log = await Execute(new Pipeline());
            Assert.Contains(log, m => m.Contains("ステップがありません"));
        }

        [Fact]
        public async Task SingleStep_ReportsCompletion()
        {
            var step = new GenerateRowsStep { Name = "Gen" };
            step.Settings["Fields"]   = "X=hello";
            step.Settings["RowCount"] = "3";

            var pipeline = new Pipeline();
            pipeline.Steps.Add(step);

            var log = await Execute(pipeline);

            // ExecutionEngine: "[Gen] 完了 (3行)"
            Assert.Contains(log, m => m.Contains("Gen") && m.Contains("3行"));
        }

        [Fact]
        public async Task TwoStepsConnected_DataFlows()
        {
            var gen = new GenerateRowsStep { Name = "Gen" };
            gen.Settings["Fields"]   = "STATUS=ok";
            gen.Settings["RowCount"] = "2";

            var dummy = new DummyStep { Name = "Dummy" };

            var pipeline = new Pipeline();
            pipeline.Steps.Add(gen);
            pipeline.Steps.Add(dummy);
            pipeline.Connections.Add(new PipelineConnection
            {
                SourceStepId = gen.Id,
                TargetStepId = dummy.Id
            });

            var log = await Execute(pipeline);

            // Dummy は入力をそのまま返すので2行になる
            Assert.Contains(log, m => m.Contains("Dummy") && m.Contains("2行"));
        }

        [Fact]
        public async Task SetVariable_OutputsOneRow_ReachedByNextStep()
        {
            var setVar = new SetVariableStep { Name = "Vars" };
            setVar.Settings["Fields"]     = "FROM_DATE=TODAY-7\nTO_DATE=TODAY";
            setVar.Settings["DateFormat"] = "yyyy/MM/dd";

            var dummy = new DummyStep { Name = "Pass" };
            var pipeline = new Pipeline();
            pipeline.Steps.Add(setVar);
            pipeline.Steps.Add(dummy);
            pipeline.Connections.Add(new PipelineConnection
            {
                SourceStepId = setVar.Id,
                TargetStepId = dummy.Id
            });

            var log = await Execute(pipeline);

            Assert.Contains(log, m => m.Contains("Pass") && m.Contains("1行"));
        }

        [Fact]
        public async Task Cancellation_ThrowsOperationCanceled()
        {
            var step = new GenerateRowsStep { Name = "Gen" };
            step.Settings["Fields"]   = "X=1";
            step.Settings["RowCount"] = "1";

            var pipeline = new Pipeline();
            pipeline.Steps.Add(step);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => Execute(pipeline, cts.Token));
        }
    }
}
