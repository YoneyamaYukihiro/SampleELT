using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Models;

namespace BreezeFlow.Steps
{
    /// <summary>
    /// 条件に一致する行のみを通過させるステップ。
    /// Settings["FieldName"]  : 左辺フィールド名
    /// Settings["Operator"]   : equals / notEquals / contains / greaterThan / greaterOrEqual / lessThan / lessOrEqual / isNull / isNotNull
    /// Settings["Value"]      : 比較する値（リテラル）
    /// Settings["RightField"] : 右辺フィールド名（設定時は Value より優先し、フィールド同士を比較する）
    /// </summary>
    public class FilterStep : StepBase
    {
        public override StepType StepType => StepType.Filter;

        public override async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var fieldName  = Settings.TryGetValue("FieldName",  out var fn) ? fn?.ToString()  ?? "" : "";
            var op         = Settings.TryGetValue("Operator",   out var oper) ? oper?.ToString() ?? "equals" : "equals";
            var value      = Settings.TryGetValue("Value",      out var v)  ? v?.ToString()   ?? "" : "";
            var rightField = Settings.TryGetValue("RightField", out var rf) ? rf?.ToString()  ?? "" : "";

            int matched = 0;
            int total = 0;
            await foreach (var row in input.WithCancellation(ct).ConfigureAwait(false))
            {
                total++;
                if (MatchesFilter(row, fieldName, op, value, rightField))
                {
                    matched++;
                    yield return row;
                }
            }
            progress.Report($"Filter: {matched}/{total}行 マッチ");
        }

        private static bool MatchesFilter(
            Dictionary<string, object?> row,
            string fieldName,
            string op,
            string value,
            string rightField)
        {
            if (!row.TryGetValue(fieldName, out var leftValue))
                return false;

            // isNull / isNotNull は右辺不要
            if (op == "isNull")
                return leftValue == null || leftValue == DBNull.Value;
            if (op == "isNotNull")
                return leftValue != null && leftValue != DBNull.Value;

            if (leftValue == null || leftValue == DBNull.Value)
                return false;

            // 右辺の値を決定（RightField が指定されていればそのフィールド値を使う）
            object? rightValue = string.IsNullOrEmpty(rightField)
                ? (object?)value
                : (row.TryGetValue(rightField, out var rv) ? rv : null);

            if (rightValue == null || rightValue == DBNull.Value)
                return false;

            // どちらかが DateTime なら、もう一方も DateTime に解釈して比較
            DateTime? leftAsDate = leftValue as DateTime? ?? TryParseDate(leftValue);
            DateTime? rightAsDate = rightValue as DateTime? ?? TryParseDate(rightValue);
            if (leftAsDate.HasValue && rightAsDate.HasValue)
            {
                var cmp = leftAsDate.Value.CompareTo(rightAsDate.Value);
                return op switch
                {
                    "equals"         => cmp == 0,
                    "notEquals"      => cmp != 0,
                    "greaterThan"    => cmp > 0,
                    "greaterOrEqual" => cmp >= 0,
                    "lessThan"       => cmp < 0,
                    "lessOrEqual"    => cmp <= 0,
                    _                => false
                };
            }

            // 文字列 / 数値比較
            var leftStr  = leftValue.ToString()  ?? "";
            var rightStr = rightValue.ToString() ?? "";

            return op switch
            {
                "equals"         => string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
                "notEquals"      => !string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
                "contains"       => leftStr.Contains(rightStr, StringComparison.OrdinalIgnoreCase),
                "greaterThan"    => CompareValues(leftStr, rightStr) > 0,
                "greaterOrEqual" => CompareValues(leftStr, rightStr) >= 0,
                "lessThan"       => CompareValues(leftStr, rightStr) < 0,
                "lessOrEqual"    => CompareValues(leftStr, rightStr) <= 0,
                _                => false
            };
        }

        private static DateTime? TryParseDate(object? v)
        {
            if (v == null || v == DBNull.Value) return null;
            if (v is DateTime dt) return dt;
            var s = v.ToString();
            if (string.IsNullOrEmpty(s)) return null;
            // 一般的な日付フォーマットを許容（yyyy/MM/dd, yyyy-MM-dd, yyyy/MM/dd HH:mm:ss など）
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                                  System.Globalization.DateTimeStyles.AssumeLocal, out var parsed))
                return parsed;
            return null;
        }

        private static int CompareValues(string left, string right)
        {
            if (double.TryParse(left,  out var dLeft)
             && double.TryParse(right, out var dRight))
                return dLeft.CompareTo(dRight);
            return string.Compare(left, right, StringComparison.Ordinal);
        }

        public override string GetDisplayIcon() => "🔍";
    }
}
