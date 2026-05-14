using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
    ///
    /// 実装メモ: Right 側を Dict&lt;key, List&lt;row&gt;&gt; にビルドし (O(|Right|)) 、
    /// Left 側はストリームのまま 1 行ずつ消費する。CROSS JOIN のみ Right を List で保持して
    /// 繰り返し参照する (フルテーブル積)。
    /// </summary>
    public class MergeJoinStep : StepBase
    {
        public override StepType StepType => StepType.MergeJoin;

        public override async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var joinType = Settings.TryGetValue("JoinType", out var jt) ? jt?.ToString() ?? "INNER" : "INNER";
            var keyFieldsRaw = Settings.TryGetValue("KeyFields", out var kf) ? kf?.ToString() ?? "" : "";

            var keyFields = keyFieldsRaw.Split(',')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();

            var leftStream  = AllInputStreams.Count > 0 ? AllInputStreams[0] : EmptyAsync();
            var rightStream = AllInputStreams.Count > 1 ? AllInputStreams[1] : EmptyAsync();

            // CROSS join: Right を全件 List 化して Left の各行に対し総当り
            if (keyFields.Count == 0)
            {
                var rightAll = new List<Dictionary<string, object?>>();
                await foreach (var r in rightStream.WithCancellation(ct).ConfigureAwait(false))
                    rightAll.Add(r);

                int crossCount = 0;
                await foreach (var l in leftStream.WithCancellation(ct).ConfigureAwait(false))
                {
                    foreach (var r in rightAll)
                    {
                        crossCount++;
                        yield return MergeRows(l, r);
                    }
                }
                progress.Report($"Merge Join (CROSS): {crossCount}行");
                yield break;
            }

            // Right をキーで索引化 (O(|Right|))
            var rightLookup = new Dictionary<string, List<Dictionary<string, object?>>>();
            await foreach (var r in rightStream.WithCancellation(ct).ConfigureAwait(false))
            {
                var key = BuildKey(r, keyFields);
                if (!rightLookup.TryGetValue(key, out var bucket))
                    rightLookup[key] = bucket = new List<Dictionary<string, object?>>();
                bucket.Add(r);
            }

            var matchedRightKeys = new HashSet<string>();
            int emitted = 0;

            // Left をストリーミング消費
            await foreach (var leftRow in leftStream.WithCancellation(ct).ConfigureAwait(false))
            {
                var key = BuildKey(leftRow, keyFields);
                if (rightLookup.TryGetValue(key, out var matches))
                {
                    matchedRightKeys.Add(key);
                    foreach (var rightRow in matches)
                    {
                        emitted++;
                        yield return MergeRows(leftRow, rightRow);
                    }
                }
                else if (joinType is "LEFT OUTER" or "FULL OUTER")
                {
                    emitted++;
                    yield return MergeRows(leftRow, null);
                }
            }

            // RIGHT / FULL OUTER: 未マッチの Right 行も出力
            if (joinType is "RIGHT OUTER" or "FULL OUTER")
            {
                foreach (var kv in rightLookup)
                {
                    if (matchedRightKeys.Contains(kv.Key)) continue;
                    foreach (var rightRow in kv.Value)
                    {
                        ct.ThrowIfCancellationRequested();
                        emitted++;
                        yield return MergeRows(null, rightRow);
                    }
                }
            }

            progress.Report($"Merge Join ({joinType}): {emitted}行");
        }

        private static async IAsyncEnumerable<Dictionary<string, object?>> EmptyAsync()
        {
            await Task.CompletedTask;
            yield break;
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
