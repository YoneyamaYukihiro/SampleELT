using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class CalculationStep : StepBase
    {
        public override StepType StepType => StepType.Calculation;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var outputFieldName = Settings.TryGetValue("OutputFieldName", out var ofn) ? ofn?.ToString() ?? "Result" : "Result";
            var expressionType = Settings.TryGetValue("ExpressionType", out var et) ? et?.ToString() ?? "add" : "add";
            var field1 = Settings.TryGetValue("Field1", out var f1) ? f1?.ToString() ?? "" : "";
            var field2 = Settings.TryGetValue("Field2", out var f2) ? f2?.ToString() ?? "" : "";
            var constant = Settings.TryGetValue("Constant", out var c) ? c?.ToString() ?? "0" : "0";

            var result = new List<Dictionary<string, object?>>();

            await Task.Run(() =>
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var newRow = new Dictionary<string, object?>(row);
                    newRow[outputFieldName] = ComputeExpression(row, expressionType, field1, field2, constant);
                    result.Add(newRow);
                }
            }, ct);

            progress.Report($"Calculation: {result.Count}行 計算完了 ({outputFieldName})");
            return result;
        }

        private static object? ComputeExpression(
            Dictionary<string, object?> row,
            string expressionType,
            string field1,
            string field2,
            string constant)
        {
            object? GetFieldValue(string fieldName)
            {
                return row.TryGetValue(fieldName, out var v) ? v : null;
            }

            double GetNumeric(object? val)
            {
                if (val == null || val == DBNull.Value) return 0;
                return double.TryParse(val.ToString(), out var d) ? d : 0;
            }

            switch (expressionType)
            {
                case "add":
                {
                    var v1 = GetNumeric(GetFieldValue(field1));
                    var v2 = GetNumeric(GetFieldValue(field2));
                    return v1 + v2;
                }
                case "subtract":
                {
                    var v1 = GetNumeric(GetFieldValue(field1));
                    var v2 = GetNumeric(GetFieldValue(field2));
                    return v1 - v2;
                }
                case "multiply":
                {
                    var v1 = GetNumeric(GetFieldValue(field1));
                    var v2 = GetNumeric(GetFieldValue(field2));
                    return v1 * v2;
                }
                case "divide":
                {
                    var v1 = GetNumeric(GetFieldValue(field1));
                    var v2 = GetNumeric(GetFieldValue(field2));
                    return v2 != 0 ? v1 / v2 : (object?)null;
                }
                case "concat":
                {
                    var s1 = GetFieldValue(field1)?.ToString() ?? "";
                    var s2 = GetFieldValue(field2)?.ToString() ?? "";
                    return s1 + s2;
                }
                case "constant":
                {
                    if (double.TryParse(constant, out var d))
                        return d;
                    return constant;
                }
                default:
                    return null;
            }
        }

        public override string GetDisplayIcon() => "🧮";
    }
}
