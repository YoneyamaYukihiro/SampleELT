using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Steps
{
    /// <summary>
    /// 条件に一致する行のみを通過させるステップ。
    /// Settings["FieldName"]  : 左辺フィールド名
    /// Settings["Operator"]   : equals / notEquals / contains / greaterThan / lessThan / isNull / isNotNull
    /// Settings["Value"]      : 比較する値（リテラル）
    /// Settings["RightField"] : 右辺フィールド名（設定時は Value より優先し、フィールド同士を比較する）
    /// </summary>
    public class FilterStep : StepBase
    {
        public override StepType StepType => StepType.Filter;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var fieldName  = Settings.TryGetValue("FieldName",  out var fn) ? fn?.ToString()  ?? "" : "";
            var op         = Settings.TryGetValue("Operator",   out var oper) ? oper?.ToString() ?? "equals" : "equals";
            var value      = Settings.TryGetValue("Value",      out var v)  ? v?.ToString()   ?? "" : "";
            var rightField = Settings.TryGetValue("RightField", out var rf) ? rf?.ToString()  ?? "" : "";

            var result = new List<Dictionary<string, object?>>();

            await Task.Run(() =>
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();

                    if (MatchesFilter(row, fieldName, op, value, rightField))
                        result.Add(row);
                }
            }, ct);

            progress.Report($"Filter: {result.Count}/{inputData.Count}行 マッチ");
            return result;
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

            // DateTime 同士の比較
            if (leftValue is DateTime leftDt && rightValue is DateTime rightDt)
            {
                return op switch
                {
                    "equals"      => leftDt == rightDt,
                    "notEquals"   => leftDt != rightDt,
                    "greaterThan" => leftDt > rightDt,
                    "lessThan"    => leftDt < rightDt,
                    _             => false
                };
            }

            // 文字列 / 数値比較
            var leftStr  = leftValue.ToString()  ?? "";
            var rightStr = rightValue.ToString() ?? "";

            return op switch
            {
                "equals"      => string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
                "notEquals"   => !string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
                "contains"    => leftStr.Contains(rightStr, StringComparison.OrdinalIgnoreCase),
                "greaterThan" => CompareValues(leftStr, rightStr) > 0,
                "lessThan"    => CompareValues(leftStr, rightStr) < 0,
                _             => false
            };
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
