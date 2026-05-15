using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Steps;
using Xunit;

namespace BreezeFlow.Tests.Steps
{
    /// <summary>
    /// TableCompareStep の差分判定と出力スキーマを検証する。
    /// </summary>
    public class TableCompareStepTests
    {
        private static Dictionary<string, object?> Row(params (string k, object? v)[] kv)
        {
            var d = new Dictionary<string, object?>();
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        private static async IAsyncEnumerable<Dictionary<string, object?>> AsAsync(
            List<Dictionary<string, object?>> source)
        {
            foreach (var r in source) yield return r;
            await Task.CompletedTask;
        }

        private static TableCompareStep Build(
            List<Dictionary<string, object?>> left,
            List<Dictionary<string, object?>> right,
            string keyFields,
            string compareFields = "",
            bool includeMatched = false,
            bool nullsEqual = true,
            bool ignoreCase = false,
            bool trimStrings = false)
        {
            var step = new TableCompareStep
            {
                Name = "compare",
                AllInputStreams = new List<IAsyncEnumerable<Dictionary<string, object?>>> { AsAsync(left), AsAsync(right) }
            };
            step.Settings["KeyFields"]      = keyFields;
            step.Settings["CompareFields"]  = compareFields;
            step.Settings["IncludeMatched"] = includeMatched ? "true" : "false";
            step.Settings["NullsEqual"]     = nullsEqual     ? "true" : "false";
            step.Settings["IgnoreCase"]     = ignoreCase     ? "true" : "false";
            step.Settings["TrimStrings"]    = trimStrings    ? "true" : "false";
            return step;
        }

        private static List<Dictionary<string, object?>> Run(TableCompareStep step)
            => step.ExecuteAsync(new List<Dictionary<string, object?>>(),
                                 new Progress<string>(), CancellationToken.None).GetAwaiter().GetResult();

        [Fact]
        public void Match_AllSame_NoOutput_WhenIncludeMatchedFalse()
        {
            var left  = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "A")) };
            var right = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "A")) };

            var result = Run(Build(left, right, "id"));
            Assert.Empty(result);
        }

        [Fact]
        public void Match_AllSame_IncludesRow_WhenIncludeMatchedTrue()
        {
            var left  = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "A")) };
            var right = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "A")) };

            var result = Run(Build(left, right, "id", includeMatched: true));
            Assert.Single(result);
            Assert.Equal("MATCH", result[0]["_status"]);
        }

        [Fact]
        public void Diff_DetectsDifferingColumn()
        {
            var left  = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "A"), ("status", "OK")) };
            var right = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "A"), ("status", "NG")) };

            var result = Run(Build(left, right, "id"));
            Assert.Single(result);
            Assert.Equal("DIFF", result[0]["_status"]);
            Assert.Equal("status", result[0]["_diff_columns"]);
            Assert.Equal("OK", result[0]["status_L"]);
            Assert.Equal("NG", result[0]["status_R"]);
        }

        [Fact]
        public void OnlyLeft_LeftHasNoMatchInRight()
        {
            var left  = new List<Dictionary<string, object?>> { Row(("id", 99), ("name", "Alone")) };
            var right = new List<Dictionary<string, object?>>();

            var result = Run(Build(left, right, "id"));
            Assert.Single(result);
            Assert.Equal("ONLY_LEFT", result[0]["_status"]);
            Assert.Equal(99, result[0]["id"]);
            Assert.Equal("Alone", result[0]["name_L"]);
            Assert.Null(result[0]["name_R"]);
        }

        [Fact]
        public void OnlyRight_RightHasNoMatchInLeft()
        {
            var left  = new List<Dictionary<string, object?>>();
            var right = new List<Dictionary<string, object?>> { Row(("id", 7), ("name", "Extra")) };

            var result = Run(Build(left, right, "id"));
            Assert.Single(result);
            Assert.Equal("ONLY_RIGHT", result[0]["_status"]);
            Assert.Equal(7, result[0]["id"]);
            Assert.Null(result[0]["name_L"]);
            Assert.Equal("Extra", result[0]["name_R"]);
        }

        [Fact]
        public void CompareFields_RestrictsColumnsInspected()
        {
            // status は違うが、CompareFields=name のため DIFF にならない
            var left  = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "A"), ("status", "OK")) };
            var right = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "A"), ("status", "NG")) };

            var result = Run(Build(left, right, "id", compareFields: "name"));
            Assert.Empty(result); // MATCH 行は省略
        }

        [Fact]
        public void MultipleKey_HandlesCompositeKey()
        {
            var left  = new List<Dictionary<string, object?>>
            {
                Row(("dept", 1), ("emp", 100), ("v", 10)),
                Row(("dept", 2), ("emp", 200), ("v", 20)),
            };
            var right = new List<Dictionary<string, object?>>
            {
                Row(("dept", 1), ("emp", 100), ("v", 10)),
                Row(("dept", 2), ("emp", 200), ("v", 99)),  // diff
            };

            var result = Run(Build(left, right, "dept,emp"));
            Assert.Single(result);
            Assert.Equal("DIFF", result[0]["_status"]);
            Assert.Equal(2, result[0]["dept"]);
            Assert.Equal(200, result[0]["emp"]);
        }

        [Fact]
        public void NumericComparison_OneVsOnePointZero_AreEqual()
        {
            var left  = new List<Dictionary<string, object?>> { Row(("id", 1), ("amt", 100)) };
            var right = new List<Dictionary<string, object?>> { Row(("id", 1), ("amt", 100.0)) };

            var result = Run(Build(left, right, "id", includeMatched: true));
            Assert.Single(result);
            Assert.Equal("MATCH", result[0]["_status"]);
        }

        [Fact]
        public void NullValues_NullsEqualTrue_AreMatched()
        {
            var left  = new List<Dictionary<string, object?>> { Row(("id", 1), ("memo", null)) };
            var right = new List<Dictionary<string, object?>> { Row(("id", 1), ("memo", null)) };

            var result = Run(Build(left, right, "id"));
            Assert.Empty(result);
        }

        [Fact]
        public void NullValues_NullsEqualFalse_AreDifferent()
        {
            var left  = new List<Dictionary<string, object?>> { Row(("id", 1), ("memo", null)) };
            var right = new List<Dictionary<string, object?>> { Row(("id", 1), ("memo", null)) };

            var result = Run(Build(left, right, "id", nullsEqual: false));
            Assert.Single(result);
            Assert.Equal("DIFF", result[0]["_status"]);
        }

        [Fact]
        public void IgnoreCase_StringDifferOnlyByCase_IsMatch()
        {
            var left  = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "alice")) };
            var right = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "ALICE")) };

            // 既定 (ignoreCase=false) では DIFF
            Assert.Single(Run(Build(left, right, "id")));
            // ignoreCase=true で MATCH
            Assert.Empty(Run(Build(left, right, "id", ignoreCase: true)));
        }

        [Fact]
        public void TrimStrings_LeadingTrailingWhitespace_IsMatch()
        {
            var left  = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "  Alice  ")) };
            var right = new List<Dictionary<string, object?>> { Row(("id", 1), ("name", "Alice")) };

            Assert.Single(Run(Build(left, right, "id")));
            Assert.Empty(Run(Build(left, right, "id", trimStrings: true)));
        }

        [Fact]
        public void NoKeyFields_Throws()
        {
            var left  = new List<Dictionary<string, object?>> { Row(("id", 1)) };
            var right = new List<Dictionary<string, object?>>();

            var step = Build(left, right, ""); // KeyFields 空
            Assert.Throws<InvalidOperationException>(() => Run(step));
        }

        [Fact]
        public void InferCompareFields_WhenCompareFieldsEmpty_UsesAllNonKeyColumns()
        {
            var left  = new List<Dictionary<string, object?>> { Row(("id", 1), ("a", 1), ("b", 2)) };
            var right = new List<Dictionary<string, object?>> { Row(("id", 1), ("a", 1), ("b", 99)) };

            var result = Run(Build(left, right, "id"));
            Assert.Single(result);
            Assert.Equal("DIFF", result[0]["_status"]);
            Assert.Equal("b", result[0]["_diff_columns"]);
        }

        [Fact]
        public void Summary_Counts_AllFourStatuses()
        {
            var left = new List<Dictionary<string, object?>>
            {
                Row(("id", 1), ("v", "x")),  // MATCH
                Row(("id", 2), ("v", "x")),  // DIFF (right v=y)
                Row(("id", 3), ("v", "x")),  // ONLY_LEFT
            };
            var right = new List<Dictionary<string, object?>>
            {
                Row(("id", 1), ("v", "x")),
                Row(("id", 2), ("v", "y")),
                Row(("id", 9), ("v", "z")),  // ONLY_RIGHT
            };

            var result = Run(Build(left, right, "id", includeMatched: true));
            var byStatus = result
                .GroupBy(r => r["_status"]?.ToString() ?? "")
                .ToDictionary(g => g.Key, g => g.Count());

            Assert.Equal(1, byStatus.GetValueOrDefault("MATCH"));
            Assert.Equal(1, byStatus.GetValueOrDefault("DIFF"));
            Assert.Equal(1, byStatus.GetValueOrDefault("ONLY_LEFT"));
            Assert.Equal(1, byStatus.GetValueOrDefault("ONLY_RIGHT"));
        }
    }
}
