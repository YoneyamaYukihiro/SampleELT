using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class FilterStep : StepBase
    {
        public override StepType StepType => StepType.Filter;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var fieldName = Settings.TryGetValue("FieldName", out var fn) ? fn?.ToString() ?? "" : "";
            var op = Settings.TryGetValue("Operator", out var oper) ? oper?.ToString() ?? "equals" : "equals";
            var value = Settings.TryGetValue("Value", out var v) ? v?.ToString() ?? "" : "";

            var result = new List<Dictionary<string, object?>>();

            await Task.Run(() =>
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();

                    if (MatchesFilter(row, fieldName, op, value))
                    {
                        result.Add(row);
                    }
                }
            }, ct);

            progress.Report($"Filter: {result.Count}/{inputData.Count}行 マッチ");
            return result;
        }

        private static bool MatchesFilter(Dictionary<string, object?> row, string fieldName, string op, string value)
        {
            if (!row.TryGetValue(fieldName, out var fieldValue))
                return false;

            switch (op)
            {
                case "isNull":
                    return fieldValue == null || fieldValue == DBNull.Value;

                case "isNotNull":
                    return fieldValue != null && fieldValue != DBNull.Value;

                default:
                    if (fieldValue == null || fieldValue == DBNull.Value)
                        return false;

                    var fieldStr = fieldValue.ToString() ?? "";

                    switch (op)
                    {
                        case "equals":
                            return string.Equals(fieldStr, value, StringComparison.OrdinalIgnoreCase);

                        case "notEquals":
                            return !string.Equals(fieldStr, value, StringComparison.OrdinalIgnoreCase);

                        case "contains":
                            return fieldStr.Contains(value, StringComparison.OrdinalIgnoreCase);

                        case "greaterThan":
                            if (double.TryParse(fieldStr, out var dField) && double.TryParse(value, out var dValue))
                                return dField > dValue;
                            return string.Compare(fieldStr, value, StringComparison.Ordinal) > 0;

                        case "lessThan":
                            if (double.TryParse(fieldStr, out var dField2) && double.TryParse(value, out var dValue2))
                                return dField2 < dValue2;
                            return string.Compare(fieldStr, value, StringComparison.Ordinal) < 0;

                        default:
                            return false;
                    }
            }
        }

        public override string GetDisplayIcon() => "🔍";
    }
}
