using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Models;

namespace BreezeFlow.Steps
{
    /// <summary>
    /// 2 つの入力ストリーム (Left / Right) をキーで突き合わせ、対応する行が両方にあるか・
    /// 列の値が一致するかを判定して差分を抽出するステップ。データ移行時の事前 / 事後検証用。
    ///
    /// <para>
    /// 入力: MergeJoin と同じく <c>AllInputStreams[0]</c> = Left、<c>AllInputStreams[1]</c> = Right。
    /// </para>
    ///
    /// <para>出力行のスキーマ:
    /// <list type="bullet">
    /// <item>キー列 (KeyFields で指定した列名そのまま) — 該当側に値があれば設定</item>
    /// <item>各比較列 <c>col</c> について <c>col_L</c> / <c>col_R</c> — Left / Right 側の値</item>
    /// <item><c>_status</c>: <c>MATCH</c> / <c>DIFF</c> / <c>ONLY_LEFT</c> / <c>ONLY_RIGHT</c></item>
    /// <item><c>_diff_columns</c>: DIFF 行のみ、差異のある比較列名をカンマ区切り</item>
    /// </list>
    /// </para>
    ///
    /// <para>Settings:
    /// <list type="bullet">
    /// <item><c>KeyFields</c>: カンマ区切りのキー列名 (必須)</item>
    /// <item><c>CompareFields</c>: カンマ区切りの比較列名 (省略時は両側の全列 - キー列)</item>
    /// <item><c>IgnoreCase</c>: 文字列を大文字小文字無視で比較 (既定 false)</item>
    /// <item><c>TrimStrings</c>: 比較前に前後空白を除去 (既定 false)</item>
    /// <item><c>NullsEqual</c>: 両側 NULL を等しいとみなす (既定 true)</item>
    /// <item><c>IncludeMatched</c>: MATCH 行も出力する (既定 false。差分の確認用途)</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// 実装メモ: Right を Dict&lt;key, row&gt; に索引化 (O(|Right|)) し、Left はストリームのまま
    /// 1 行ずつ消費する。比較列が未指定で Left の最初の行と Right の最初の行から推測する場合の
    /// 「最初の行」は、Left は最初の 1 行を覗き見、Right は索引化後の任意の 1 行を使う。
    /// </para>
    /// </summary>
    public class TableCompareStep : StepBase
    {
        public const string StatusMatch     = "MATCH";
        public const string StatusDiff      = "DIFF";
        public const string StatusOnlyLeft  = "ONLY_LEFT";
        public const string StatusOnlyRight = "ONLY_RIGHT";

        public override StepType StepType => StepType.TableCompare;

        public override async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var keyFields = ParseCsv(GetSetting("KeyFields"));
            var compareFieldsRaw = ParseCsv(GetSetting("CompareFields"));
            var ignoreCase  = GetBool("IgnoreCase",  false);
            var trimStrings = GetBool("TrimStrings", false);
            var nullsEqual  = GetBool("NullsEqual",  true);
            var includeMatched = GetBool("IncludeMatched", false);

            if (keyFields.Count == 0)
                throw new InvalidOperationException(
                    "TableCompare: KeyFields が空です。突き合わせに使うキー列名 (カンマ区切り) を指定してください。");

            var leftStream  = AllInputStreams.Count > 0 ? AllInputStreams[0] : EmptyAsync();
            var rightStream = AllInputStreams.Count > 1 ? AllInputStreams[1] : EmptyAsync();

            // Right をキーで索引化 (重複キーは最初の 1 件のみ)
            var rightLookup = new Dictionary<string, Dictionary<string, object?>>();
            int dupRightKeys = 0;
            await foreach (var r in rightStream.WithCancellation(ct).ConfigureAwait(false))
            {
                var key = BuildKey(r, keyFields);
                if (!rightLookup.TryAdd(key, r))
                    dupRightKeys++;
            }
            if (dupRightKeys > 0)
                progress.Report($"TableCompare: Right 側にキー重複が {dupRightKeys} 件あります (初出のみを比較対象にしました)");

            // 比較列の推測用に Right から代表行を 1 つ拾っておく (Left は後でストリームから取得)
            var rightSample = rightLookup.Values.FirstOrDefault();

            var seenRightKeys = new HashSet<string>();
            int matched = 0, diff = 0, onlyL = 0, onlyR = 0;

            // 比較列リスト (Left を読みながら最初の 1 行で確定するために遅延初期化する)
            List<string>? compareFields = compareFieldsRaw.Count > 0 ? compareFieldsRaw : null;

            // Left をストリーミング消費
            await foreach (var left in leftStream.WithCancellation(ct).ConfigureAwait(false))
            {
                if (compareFields == null)
                    compareFields = InferCompareFields(left, rightSample, keyFields);

                var key = BuildKey(left, keyFields);
                if (rightLookup.TryGetValue(key, out var right))
                {
                    seenRightKeys.Add(key);

                    var diffCols = new List<string>();
                    foreach (var col in compareFields)
                    {
                        var lv = left.TryGetValue(col, out var lvObj) ? lvObj : null;
                        var rv = right.TryGetValue(col, out var rvObj) ? rvObj : null;
                        if (!ValuesEqual(lv, rv, ignoreCase, trimStrings, nullsEqual))
                            diffCols.Add(col);
                    }

                    if (diffCols.Count > 0)
                    {
                        diff++;
                        yield return BuildOutputRow(keyFields, compareFields, left, right, StatusDiff, string.Join(",", diffCols));
                    }
                    else
                    {
                        matched++;
                        if (includeMatched)
                            yield return BuildOutputRow(keyFields, compareFields, left, right, StatusMatch, "");
                    }
                }
                else
                {
                    onlyL++;
                    yield return BuildOutputRow(keyFields, compareFields, left, null, StatusOnlyLeft, "");
                }
            }

            // Left が空でも compareFields を確定させる必要がある (ONLY_RIGHT 出力のため)
            if (compareFields == null)
                compareFields = compareFieldsRaw.Count > 0
                    ? compareFieldsRaw
                    : InferCompareFields(null, rightSample, keyFields);

            // Right に残った (Left に対応行が無い) ものを出力
            foreach (var kv in rightLookup)
            {
                ct.ThrowIfCancellationRequested();
                if (seenRightKeys.Contains(kv.Key)) continue;
                onlyR++;
                yield return BuildOutputRow(keyFields, compareFields, null, kv.Value, StatusOnlyRight, "");
            }

            progress.Report(
                $"TableCompare: MATCH {matched} / DIFF {diff} / ONLY_LEFT {onlyL} / ONLY_RIGHT {onlyR}" +
                (includeMatched ? "" : "  (MATCH 行は省略)"));
        }

        private static async IAsyncEnumerable<Dictionary<string, object?>> EmptyAsync()
        {
            await Task.CompletedTask;
            yield break;
        }

        private static List<string> ParseCsv(string s)
            => string.IsNullOrWhiteSpace(s)
                ? new List<string>()
                : s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        private string GetSetting(string key)
            => Settings.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

        private bool GetBool(string key, bool defaultValue)
        {
            if (!Settings.TryGetValue(key, out var v) || v == null) return defaultValue;
            return bool.TryParse(v.ToString(), out var b) ? b : defaultValue;
        }

        /// <summary>Left/Right の最初の行から「両側で観測された列名 - キー列」を抽出。null 引数は無視。</summary>
        private static List<string> InferCompareFields(
            Dictionary<string, object?>? leftSample,
            Dictionary<string, object?>? rightSample,
            List<string> keyFields)
        {
            var keys = new HashSet<string>(keyFields, StringComparer.Ordinal);
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void Add(IEnumerable<string>? names)
            {
                if (names == null) return;
                foreach (var n in names)
                    if (!keys.Contains(n) && seen.Add(n))
                        ordered.Add(n);
            }

            Add(leftSample?.Keys);
            Add(rightSample?.Keys);
            return ordered;
        }

        private static string BuildKey(Dictionary<string, object?> row, List<string> keyFields)
            => string.Join("|", keyFields.Select(k =>
                row.TryGetValue(k, out var v) ? v?.ToString() ?? "<null>" : "<missing>"));

        private static Dictionary<string, object?> BuildOutputRow(
            List<string> keyFields,
            List<string> compareFields,
            Dictionary<string, object?>? left,
            Dictionary<string, object?>? right,
            string status,
            string diffColumns)
        {
            var row = new Dictionary<string, object?>();

            // キー列: 左を優先、無ければ右
            foreach (var k in keyFields)
            {
                object? v = null;
                if (left != null && left.TryGetValue(k, out var lv)) v = lv;
                else if (right != null && right.TryGetValue(k, out var rv)) v = rv;
                row[k] = v;
            }

            foreach (var col in compareFields)
            {
                row[$"{col}_L"] = left  != null && left.TryGetValue(col,  out var lv) ? lv : null;
                row[$"{col}_R"] = right != null && right.TryGetValue(col, out var rv) ? rv : null;
            }

            row["_status"] = status;
            row["_diff_columns"] = diffColumns;
            return row;
        }

        /// <summary>
        /// 2 つの値が等しいか比較する。数値同士なら decimal で、それ以外は文字列化して比較する。
        /// オプションで NULL 等価 / 大文字小文字無視 / 前後空白除去 を考慮する。
        /// </summary>
        internal static bool ValuesEqual(
            object? left, object? right,
            bool ignoreCase, bool trimStrings, bool nullsEqual)
        {
            bool lNull = left  is null or DBNull;
            bool rNull = right is null or DBNull;
            if (lNull && rNull) return nullsEqual;
            if (lNull || rNull) return false;

            // 上の null チェックを通った時点で left/right は非 null
            var l = left!;
            var r = right!;

            // 数値型は decimal で比較 (1 と 1.0 を一致させたい)
            if (TryToDecimal(l, out var ld) && TryToDecimal(r, out var rd))
                return ld == rd;

            // 日時型はそのまま比較
            if (l is DateTime ldt && r is DateTime rdt)
                return ldt == rdt;

            var ls = l.ToString() ?? "";
            var rs = r.ToString() ?? "";
            if (trimStrings) { ls = ls.Trim(); rs = rs.Trim(); }
            return string.Equals(ls, rs,
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        private static bool TryToDecimal(object value, out decimal result)
        {
            switch (value)
            {
                case decimal d: result = d; return true;
                case int i:     result = i; return true;
                case long l:    result = l; return true;
                case short sh:  result = sh; return true;
                case double dd: result = (decimal)dd; return true;
                case float ff:  result = (decimal)ff; return true;
                default:
                    return decimal.TryParse(
                        value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
            }
        }

        public override string GetDisplayIcon() => "🔍";
    }
}
