using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Engine;
using BreezeFlow.Models;
using BreezeFlow.Steps;
using Xunit;

namespace BreezeFlow.Tests.Engine
{
    /// <summary>
    /// 多ポート (Filter pass/fail / Switch) のエンジン側ルーティングを
    /// End-to-End で検証する。
    ///
    /// 各分岐の下流に専用の <see cref="CaptureStep"/> を置いて、
    /// その分岐に何行流れたかを記録する。
    /// </summary>
    public class BranchRoutingTests
    {
        /// <summary>入力をそのまま素通しつつ内部リストに記録するテスト用ステップ。</summary>
        private class CaptureStep : StepBase
        {
            public override StepType StepType => StepType.Dummy;
            public List<Dictionary<string, object?>> Captured { get; } = new();

            public override async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
                IAsyncEnumerable<Dictionary<string, object?>> input,
                System.IProgress<string> progress,
                [EnumeratorCancellation] CancellationToken ct)
            {
                await foreach (var row in input.WithCancellation(ct).ConfigureAwait(false))
                {
                    Captured.Add(row);
                    yield return row;
                }
            }
        }

        /// <summary>固定の行列を出力するテスト用入力ステップ。</summary>
        private class SourceStep : StepBase
        {
            public override StepType StepType => StepType.Dummy;
            public List<Dictionary<string, object?>> Rows { get; } = new();

            public override async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
                IAsyncEnumerable<Dictionary<string, object?>> input,
                System.IProgress<string> progress,
                [EnumeratorCancellation] CancellationToken ct)
            {
                foreach (var row in Rows)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return row;
                }
                await Task.CompletedTask;
            }
        }

        [Fact]
        public async Task Filter_PassAndFail_RoutedToDifferentDownstreams()
        {
            var src = new SourceStep { Name = "Src" };
            src.Rows.Add(new Dictionary<string, object?> { ["v"] = "ok",  ["id"] = 1 });
            src.Rows.Add(new Dictionary<string, object?> { ["v"] = "bad", ["id"] = 2 });
            src.Rows.Add(new Dictionary<string, object?> { ["v"] = "ok",  ["id"] = 3 });

            var filter = new FilterStep { Name = "F" };
            filter.Settings["FieldName"] = "v";
            filter.Settings["Operator"]  = "equals";
            filter.Settings["Value"]     = "ok";

            var passSink = new CaptureStep { Name = "Pass" };
            var failSink = new CaptureStep { Name = "Fail" };

            var p = new Pipeline();
            p.Steps.Add(src);
            p.Steps.Add(filter);
            p.Steps.Add(passSink);
            p.Steps.Add(failSink);

            p.Connections.Add(new PipelineConnection { SourceStepId = src.Id,    TargetStepId = filter.Id });
            p.Connections.Add(new PipelineConnection { SourceStepId = filter.Id, TargetStepId = passSink.Id, SourceBranchKey = FilterStep.PassBranchKey });
            p.Connections.Add(new PipelineConnection { SourceStepId = filter.Id, TargetStepId = failSink.Id, SourceBranchKey = FilterStep.FailBranchKey });

            await new ExecutionEngine().ExecuteAsync(p, new SyncProgress(), CancellationToken.None);

            Assert.Equal(2, passSink.Captured.Count);
            Assert.All(passSink.Captured, r => Assert.Equal("ok", r["v"]));
            Assert.Single(failSink.Captured);
            Assert.Equal("bad", failSink.Captured[0]["v"]);
        }

        [Fact]
        public async Task Filter_OnlyPassWired_FailRowsAreDiscarded()
        {
            // 旧 Filter (単一出力) と等価の挙動 — fail ポートに下流が無ければ捨てる。
            var src = new SourceStep { Name = "Src" };
            src.Rows.Add(new Dictionary<string, object?> { ["v"] = "ok" });
            src.Rows.Add(new Dictionary<string, object?> { ["v"] = "bad" });
            src.Rows.Add(new Dictionary<string, object?> { ["v"] = "ok" });

            var filter = new FilterStep { Name = "F" };
            filter.Settings["FieldName"] = "v";
            filter.Settings["Operator"]  = "equals";
            filter.Settings["Value"]     = "ok";

            var passSink = new CaptureStep { Name = "Pass" };

            var p = new Pipeline();
            p.Steps.Add(src);
            p.Steps.Add(filter);
            p.Steps.Add(passSink);

            p.Connections.Add(new PipelineConnection { SourceStepId = src.Id,    TargetStepId = filter.Id });
            p.Connections.Add(new PipelineConnection { SourceStepId = filter.Id, TargetStepId = passSink.Id, SourceBranchKey = FilterStep.PassBranchKey });

            await new ExecutionEngine().ExecuteAsync(p, new SyncProgress(), CancellationToken.None);

            Assert.Equal(2, passSink.Captured.Count);
        }

        [Fact]
        public async Task Switch_DistributesRowsToMatchingPorts()
        {
            var src = new SourceStep { Name = "Src" };
            src.Rows.Add(new Dictionary<string, object?> { ["Region"] = "東京", ["X"] = 1 });
            src.Rows.Add(new Dictionary<string, object?> { ["Region"] = "大阪", ["X"] = 2 });
            src.Rows.Add(new Dictionary<string, object?> { ["Region"] = "札幌", ["X"] = 3 });

            var sw = new SwitchStep { Name = "Sw" };
            sw.Settings["FieldName"] = "Region";
            sw.Settings["Cases"] = "東京|tokyo\n大阪|osaka";

            var tokyoSink   = new CaptureStep { Name = "Tokyo" };
            var osakaSink   = new CaptureStep { Name = "Osaka" };
            var defaultSink = new CaptureStep { Name = "Other" };

            var p = new Pipeline();
            p.Steps.Add(src);
            p.Steps.Add(sw);
            p.Steps.Add(tokyoSink);
            p.Steps.Add(osakaSink);
            p.Steps.Add(defaultSink);

            p.Connections.Add(new PipelineConnection { SourceStepId = src.Id, TargetStepId = sw.Id });
            p.Connections.Add(new PipelineConnection { SourceStepId = sw.Id,  TargetStepId = tokyoSink.Id,   SourceBranchKey = "tokyo" });
            p.Connections.Add(new PipelineConnection { SourceStepId = sw.Id,  TargetStepId = osakaSink.Id,   SourceBranchKey = "osaka" });
            p.Connections.Add(new PipelineConnection { SourceStepId = sw.Id,  TargetStepId = defaultSink.Id, SourceBranchKey = SwitchStep.DefaultBranchKey });

            await new ExecutionEngine().ExecuteAsync(p, new SyncProgress(), CancellationToken.None);

            Assert.Single(tokyoSink.Captured);
            Assert.Single(osakaSink.Captured);
            Assert.Single(defaultSink.Captured);
            Assert.Equal("東京", tokyoSink.Captured[0]["Region"]);
            Assert.Equal("大阪", osakaSink.Captured[0]["Region"]);
            Assert.Equal("札幌", defaultSink.Captured[0]["Region"]);
        }

        [Fact]
        public async Task Switch_UnusedBranch_DoesNotCrash()
        {
            // tokyo ブランチに下流が無くても、Switch は全ケースを処理して例外を出さない。
            var src = new SourceStep { Name = "Src" };
            src.Rows.Add(new Dictionary<string, object?> { ["Region"] = "東京" });
            src.Rows.Add(new Dictionary<string, object?> { ["Region"] = "大阪" });

            var sw = new SwitchStep { Name = "Sw" };
            sw.Settings["FieldName"] = "Region";
            sw.Settings["Cases"] = "東京|tokyo\n大阪|osaka";

            var osakaSink = new CaptureStep { Name = "Osaka" };

            var p = new Pipeline();
            p.Steps.Add(src);
            p.Steps.Add(sw);
            p.Steps.Add(osakaSink);

            p.Connections.Add(new PipelineConnection { SourceStepId = src.Id, TargetStepId = sw.Id });
            p.Connections.Add(new PipelineConnection { SourceStepId = sw.Id,  TargetStepId = osakaSink.Id, SourceBranchKey = "osaka" });

            await new ExecutionEngine().ExecuteAsync(p, new SyncProgress(), CancellationToken.None);

            Assert.Single(osakaSink.Captured);
            Assert.Equal("大阪", osakaSink.Captured[0]["Region"]);
        }

        [Fact]
        public async Task Filter_PassFanOut_BothDownstreamsReceiveAllPassRows()
        {
            // pass ポートに 2 つの下流を繋いだ場合、両方とも同じ pass 行を受け取る (材料化済みなので)
            var src = new SourceStep { Name = "Src" };
            src.Rows.Add(new Dictionary<string, object?> { ["v"] = "ok",  ["id"] = 1 });
            src.Rows.Add(new Dictionary<string, object?> { ["v"] = "bad", ["id"] = 2 });
            src.Rows.Add(new Dictionary<string, object?> { ["v"] = "ok",  ["id"] = 3 });

            var filter = new FilterStep { Name = "F" };
            filter.Settings["FieldName"] = "v";
            filter.Settings["Operator"]  = "equals";
            filter.Settings["Value"]     = "ok";

            var s1 = new CaptureStep { Name = "S1" };
            var s2 = new CaptureStep { Name = "S2" };

            var p = new Pipeline();
            p.Steps.Add(src);
            p.Steps.Add(filter);
            p.Steps.Add(s1);
            p.Steps.Add(s2);

            p.Connections.Add(new PipelineConnection { SourceStepId = src.Id,    TargetStepId = filter.Id });
            p.Connections.Add(new PipelineConnection { SourceStepId = filter.Id, TargetStepId = s1.Id, SourceBranchKey = FilterStep.PassBranchKey });
            p.Connections.Add(new PipelineConnection { SourceStepId = filter.Id, TargetStepId = s2.Id, SourceBranchKey = FilterStep.PassBranchKey });

            await new ExecutionEngine().ExecuteAsync(p, new SyncProgress(), CancellationToken.None);

            Assert.Equal(2, s1.Captured.Count);
            Assert.Equal(2, s2.Captured.Count);
        }
    }
}
