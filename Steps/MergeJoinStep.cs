using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Steps
{
    /// <summary>
    /// 2つの入力ストリームをキーフィールドで結合するステップ。
    /// 最初に接続したストリームが Left、2番目が Right。
    /// Settings["JoinType"]  : "INNER" / "LEFT OUTER" / "RIGHT OUTER" / "FULL OUTER"
    /// Settings["KeyFields"] : カンマ区切りのキーフィールド名
    /// </summary>
    public class MergeJoinStep : StepBase
    {
        public override StepType StepType => StepType.MergeJoin;

        public override Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var joinType = Settings.TryGetValue("JoinType", out var jt) ? jt?.ToString() ?? "INNER" : "INNER";
            var keyFieldsRaw = Settings.TryGetValue("KeyFields", out var kf) ? kf?.ToString() ?? "" : "";

            var keyFields = keyFieldsRaw.Split(',')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();

            // AllInputStreams[0] = Left, AllInputStreams[1] = Right
            var leftRows = AllInputStreams.Count > 0 ? AllInputStreams[0] : new List<Dictionary<string, object?>>();
            var rightRows = AllInputStreams.Count > 1 ? AllInputStreams[1] : new List<Dictionary<string, object?>>();

            if (keyFields.Count == 0)
            {
                // No keys: cross join
                var cross = leftRows.SelectMany(l => rightRows.Select(r => MergeRows(l, r))).ToList();
                progress.Report($"Merge Join (CROSS): {cross.Count}行");
                return Task.FromResult(cross);
            }

            // Build lookup from right on key fields
            var rightLookup = new Dictionary<string, List<Dictionary<string, object?>>>();
            foreach (var r in rightRows)
            {
                var key = BuildKey(r, keyFields);
                if (!rightLookup.TryGetValue(key, out var bucket))
                    rightLookup[key] = bucket = new List<Dictionary<string, object?>>();
                bucket.Add(r);
            }

            var result = new List<Dictionary<string, object?>>();
            var matchedRightKeys = new HashSet<string>();

            foreach (var leftRow in leftRows)
            {
                ct.ThrowIfCancellationRequested();
                var key = BuildKey(leftRow, keyFields);

                if (rightLookup.TryGetValue(key, out var matches))
                {
                    matchedRightKeys.Add(key);
                    foreach (var rightRow in matches)
                        result.Add(MergeRows(leftRow, rightRow));
                }
                else if (joinType is "LEFT OUTER" or "FULL OUTER")
                {
                    result.Add(MergeRows(leftRow, null));
                }
            }

            // RIGHT / FULL OUTER: add unmatched right rows
            if (joinType is "RIGHT OUTER" or "FULL OUTER")
            {
                foreach (var rightRow in rightRows)
                {
                    ct.ThrowIfCancellationRequested();
                    var key = BuildKey(rightRow, keyFields);
                    if (!matchedRightKeys.Contains(key))
                        result.Add(MergeRows(null, rightRow));
                }
            }

            progress.Report($"Merge Join ({joinType}): {result.Count}行");
            return Task.FromResult(result);
        }

        private static string BuildKey(Dictionary<string, object?> row, List<string> keyFields)
            => string.Join("|", keyFields.Select(k => row.TryGetValue(k, out var v) ? v?.ToString() ?? "" : ""));

        private static Dictionary<string, object?> MergeRows(
            Dictionary<string, object?>? left,
            Dictionary<string, object?>? right)
        {
            var merged = new Dictionary<string, object?>();
            if (left != null)
                foreach (var kv in left) merged[kv.Key] = kv.Value;
            if (right != null)
                foreach (var kv in right)
                    merged[kv.Key] = kv.Value; // Right fields overwrite same-name left fields
            return merged;
        }

        public override string GetDisplayIcon() => "🔗";
    }
}
