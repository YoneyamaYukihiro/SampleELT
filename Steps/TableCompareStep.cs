using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Steps
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
    /// </summary>
    public class TableCompareStep : StepBase
    {
        public const string StatusMatch     = "MATCH";
        public const string StatusDiff      = "DIFF";
        public const string StatusOnlyLeft  = "ONLY_LEFT";
        public const string StatusOnlyRight = "ONLY_RIGHT";

        public override StepType StepType => StepType.TableCompare;

        public override Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var keyFields = ParseCsv(GetSetting("KeyFields"));
            var compareFieldsRaw = ParseCsv(GetSetting("CompareFields"));
            var ignoreCase  = GetBool("IgnoreCase",  false);
            var trimStrings = GetBool("TrimStrings", false);
            var nullsEqual  = GetBool("NullsEqual",  true);
            var includeMatched = GetBool("IncludeMatched", false);

            var leftRows  = AllInputStreams.Count > 0 ? AllInputStreams[0] : new List<Dictionary<string, object?>>();
            var rightRows = AllInputStreams.Count > 1 ? AllInputStreams[1] : new List<Dictionary<string, object?>>();

            if (keyFields.Count == 0)
                throw new InvalidOperationException(
                    "TableCompare: KeyFields が空です。突き合わせに使うキー列名 (カンマ区切り) を指定してください。");

            // 比較列を決める: 明示指定が無ければ「両側で観測された列名 - キー列」
            var compareFields = compareFieldsRaw.Count > 0
                ? compareFieldsRaw
                : InferCompareFields(leftRows, rightRows, keyFields);

            // Right を キー → 行リスト で索引化 (重複キーは最初の 1 件のみ突き合わせ対象とする)
            var rightLookup = new Dictionary<string, Dictionary<string, object?>>();
            var dupRightKeys = 0;
            foreach (var r in rightRows)
            {
                var key = BuildKey(r, keyFields);
                if (!rightLookup.TryAdd(key, r))
                    dupRightKeys++;
            }
            if (dupRightKeys > 0)
                progress.Report($"TableCompare: Right 側にキー重複が {dupRightKeys} 件あります (初出のみを比較対象にしました)");

            var result = new List<Dictionary<string, object?>>();
            var seenRightKeys = new HashSet<string>();
            int matched = 0, diff = 0, onlyL = 0, onlyR = 0;

            foreach (var left in leftRows)
            {
                ct.ThrowIfCancellationRequested();
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
                        result.Add(BuildOutputRow(keyFields, compareFields, left, right, StatusDiff, string.Join(",", diffCols)));
                        diff++;
                    }
                    else
                    {
                        matched++;
                        if (includeMatched)
                            result.Add(BuildOutputRow(keyFields, compareFields, left, right, StatusMatch, ""));
                    }
                }
                else
                {
                    result.Add(BuildOutputRow(keyFields, compareFields, left, null, StatusOnlyLeft, ""));
                    onlyL++;
                }
            }

            // Right に残った (Left に対応行が無い) もの
            foreach (var right in rightRows)
            {
                ct.ThrowIfCancellationRequested();
                var key = BuildKey(right, keyFields);
                if (seenRightKeys.Contains(key)) continue;
                // 重複キーで rightLookup には入らなかった行も ONLY_RIGHT として扱う必要があるが、
                // 初出を seenRightKeys に入れる場面が無いため、ここで除外する判定を追加:
                if (!rightLookup.TryGetValue(key, out var first) || !ReferenceEquals(first, right))
                    continue;

                result.Add(BuildOutputRow(keyFields, compareFields, null, right, StatusOnlyRight, ""));
                onlyR++;
            }

            progress.Report(
                $"TableCompare: MATCH {matched} / DIFF {diff} / ONLY_LEFT {onlyL} / ONLY_RIGHT {onlyR}" +
                (includeMatched ? "" : "  (MATCH 行は省略)"));

            return Task.FromResult(result);
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

        private static List<string> InferCompareFields(
            List<Dictionary<string, object?>> left,
            List<Dictionary<string, object?>> right,
            List<string> keyFields)
        {
            var keys = new HashSet<string>(keyFields, StringComparer.Ordinal);
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void Add(IEnumerable<string> names)
            {
                foreach (var n in names)
                    if (!keys.Contains(n) && seen.Add(n))
                        ordered.Add(n);
            }

            if (left.Count > 0) Add(left[0].Keys);
            if (right.Count > 0) Add(right[0].Keys);
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
