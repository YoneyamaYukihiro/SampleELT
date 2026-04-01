using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Steps;
using Xunit;

namespace SampleELT.Tests.Steps
{
    public class MergeJoinStepTests
    {
        private static List<Dictionary<string, object?>> Rows(params (string k, object? v)[][] rows)
        {
            var result = new List<Dictionary<string, object?>>();
            foreach (var row in rows)
            {
                var d = new Dictionary<string, object?>();
                foreach (var (k, v) in row) d[k] = v;
                result.Add(d);
            }
            return result;
        }

        private static async Task<List<Dictionary<string, object?>>> Join(
            List<Dictionary<string, object?>> left,
            List<Dictionary<string, object?>> right,
            string joinType, string keyFields)
        {
            var step = new MergeJoinStep();
            step.Settings["JoinType"]  = joinType;
            step.Settings["KeyFields"] = keyFields;
            step.AllInputStreams = new List<List<Dictionary<string, object?>>> { left, right };
            return await step.ExecuteAsync(left, new Progress<string>(), CancellationToken.None);
        }

        [Fact]
        public async Task InnerJoin_MatchedRowsOnly()
        {
            var left  = Rows(new[] { ("ID", (object?)"1"), ("L", (object?)"a") },
                             new[] { ("ID", (object?)"2"), ("L", (object?)"b") });
            var right = Rows(new[] { ("ID", (object?)"1"), ("R", (object?)"x") });

            var result = await Join(left, right, "INNER", "ID");

            Assert.Single(result);
            Assert.Equal("a", result[0]["L"]);
            Assert.Equal("x", result[0]["R"]);
        }

        [Fact]
        public async Task LeftOuterJoin_IncludesUnmatchedLeft()
        {
            var left  = Rows(new[] { ("ID", (object?)"1"), ("L", (object?)"a") },
                             new[] { ("ID", (object?)"2"), ("L", (object?)"b") });
            var right = Rows(new[] { ("ID", (object?)"1"), ("R", (object?)"x") });

            var result = await Join(left, right, "LEFT OUTER", "ID");

            Assert.Equal(2, result.Count);
            Assert.Contains(result, r => r["L"]?.ToString() == "b");
        }

        [Fact]
        public async Task FullOuterJoin_IncludesBothUnmatched()
        {
            var left  = Rows(new[] { ("ID", (object?)"1"), ("L", (object?)"a") });
            var right = Rows(new[] { ("ID", (object?)"2"), ("R", (object?)"y") });

            var result = await Join(left, right, "FULL OUTER", "ID");

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task InnerJoin_NoMatch_ReturnsEmpty()
        {
            var left  = Rows(new[] { ("ID", (object?)"1") });
            var right = Rows(new[] { ("ID", (object?)"9") });

            var result = await Join(left, right, "INNER", "ID");

            Assert.Empty(result);
        }

        [Fact]
        public async Task NoKeyFields_CrossJoin()
        {
            var left  = Rows(new[] { ("A", (object?)"1") }, new[] { ("A", (object?)"2") });
            var right = Rows(new[] { ("B", (object?)"x") }, new[] { ("B", (object?)"y") });

            var step = new MergeJoinStep();
            step.Settings["JoinType"]  = "INNER";
            step.Settings["KeyFields"] = "";
            step.AllInputStreams = new List<List<Dictionary<string, object?>>> { left, right };
            var result = await step.ExecuteAsync(left, new Progress<string>(), CancellationToken.None);

            Assert.Equal(4, result.Count); // 2×2 cross join
        }
    }
}
