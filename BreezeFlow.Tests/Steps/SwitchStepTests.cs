using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Models;
using BreezeFlow.Steps;
using Xunit;

namespace BreezeFlow.Tests.Steps
{
    public class SwitchStepTests
    {
        private static async Task<List<RoutedRow>> Run(
            SwitchStep step,
            IEnumerable<Dictionary<string, object?>> input)
        {
            var result = new List<RoutedRow>();
            var src = ToAsync(input);
            await foreach (var rr in step.ExecuteRoutedAsync(src, new SyncProgress(), CancellationToken.None))
                result.Add(rr);
            return result;
        }

        private static async IAsyncEnumerable<Dictionary<string, object?>> ToAsync(
            IEnumerable<Dictionary<string, object?>> src)
        {
            foreach (var row in src) yield return row;
            await Task.CompletedTask;
        }

        [Fact]
        public void OutputPorts_DerivedFromCases_PlusDefault()
        {
            var step = new SwitchStep();
            step.Settings["FieldName"] = "Region";
            step.Settings["Cases"] = "東京|tokyo\n大阪|osaka";

            var ports = step.OutputPorts;
            Assert.Equal(3, ports.Count);
            Assert.Equal("tokyo", ports[0].Key);
            Assert.Equal("osaka", ports[1].Key);
            Assert.Equal(SwitchStep.DefaultBranchKey, ports[2].Key);
        }

        [Fact]
        public void OutputPorts_NoCases_StillHasDefault()
        {
            var step = new SwitchStep();
            var ports = step.OutputPorts;
            Assert.Single(ports);
            Assert.Equal(SwitchStep.DefaultBranchKey, ports[0].Key);
        }

        [Fact]
        public void OutputPorts_IncludeDefaultFalse_OmitsDefault()
        {
            var step = new SwitchStep();
            step.Settings["FieldName"] = "X";
            step.Settings["Cases"] = "A|a\nB|b";
            step.Settings["IncludeDefault"] = "false";

            var ports = step.OutputPorts;
            Assert.Equal(2, ports.Count);
            Assert.DoesNotContain(ports, p => p.Key == SwitchStep.DefaultBranchKey);
        }

        [Fact]
        public void OutputPorts_CaseBranchKeyOmitted_UsesValueAsKey()
        {
            var step = new SwitchStep();
            step.Settings["FieldName"] = "X";
            step.Settings["Cases"] = "alpha\nbeta";

            var ports = step.OutputPorts;
            // alpha, beta, default
            Assert.Equal(3, ports.Count);
            Assert.Equal("alpha", ports[0].Key);
            Assert.Equal("beta",  ports[1].Key);
        }

        [Fact]
        public async Task ExecuteRouted_DistributesRowsByValue()
        {
            var step = new SwitchStep();
            step.Settings["FieldName"] = "Region";
            step.Settings["Cases"] = "東京|tokyo\n大阪|osaka";

            var rows = new[]
            {
                new Dictionary<string, object?> { ["Region"] = "東京", ["X"] = 1 },
                new Dictionary<string, object?> { ["Region"] = "大阪", ["X"] = 2 },
                new Dictionary<string, object?> { ["Region"] = "名古屋", ["X"] = 3 },
                new Dictionary<string, object?> { ["Region"] = "東京", ["X"] = 4 },
            };

            var result = await Run(step, rows);

            Assert.Equal(4, result.Count);
            Assert.Equal("tokyo",   result[0].BranchKey);
            Assert.Equal("osaka",   result[1].BranchKey);
            Assert.Equal(SwitchStep.DefaultBranchKey, result[2].BranchKey);
            Assert.Equal("tokyo",   result[3].BranchKey);
        }

        [Fact]
        public async Task ExecuteRouted_IncludeDefaultFalse_DropsNonMatching()
        {
            var step = new SwitchStep();
            step.Settings["FieldName"] = "Region";
            step.Settings["Cases"] = "東京|tokyo";
            step.Settings["IncludeDefault"] = "false";

            var rows = new[]
            {
                new Dictionary<string, object?> { ["Region"] = "東京" },
                new Dictionary<string, object?> { ["Region"] = "名古屋" }, // 破棄
                new Dictionary<string, object?> { ["Region"] = "東京" },
            };

            var result = await Run(step, rows);

            Assert.Equal(2, result.Count);
            Assert.All(result, r => Assert.Equal("tokyo", r.BranchKey));
        }

        [Fact]
        public async Task ExecuteRouted_NullField_GoesToDefault()
        {
            var step = new SwitchStep();
            step.Settings["FieldName"] = "Region";
            step.Settings["Cases"] = "東京|tokyo";

            var rows = new[]
            {
                new Dictionary<string, object?> { ["Region"] = null },
                new Dictionary<string, object?> { /* Region 欠落 */ ["X"] = 1 },
            };

            var result = await Run(step, rows);

            Assert.Equal(2, result.Count);
            Assert.All(result, r => Assert.Equal(SwitchStep.DefaultBranchKey, r.BranchKey));
        }

        [Fact]
        public async Task ExecuteRouted_FirstMatchWins()
        {
            // 同一値を持つ 2 つの Case を上から評価し、最初にヒットしたものが採用される
            var step = new SwitchStep();
            step.Settings["FieldName"] = "X";
            step.Settings["Cases"] = "A|first\nA|second";

            var rows = new[]
            {
                new Dictionary<string, object?> { ["X"] = "A" },
            };

            var result = await Run(step, rows);

            Assert.Single(result);
            Assert.Equal("first", result[0].BranchKey);
        }
    }
}
