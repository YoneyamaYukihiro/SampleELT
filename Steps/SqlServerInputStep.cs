using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class SqlServerInputStep : StepBase
    {
        public override StepType StepType => StepType.DBInput;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = ConnectionRegistry.Instance.ResolveConnectionString(Settings);

            var sql = Settings.TryGetValue("SQL", out var s) ? s?.ToString() ?? "" : "";
            var executeEachRow = Settings.TryGetValue("ExecuteEachRow", out var eer)
                && eer?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            var result = new List<Dictionary<string, object?>>();

            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var hasNamedParams = HasNamedParams(sql);
            var execSql = hasNamedParams ? ReplaceNamedPlaceholders(sql)
                          : sql.Contains('?') ? ReplacePositionalPlaceholders(sql) : sql;

            if (executeEachRow && inputData.Count > 0)
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    using var cmd = new SqlCommand(execSql, conn);
                    if (hasNamedParams)
                        AddNamedParameters(cmd.Parameters, row);
                    else
                        AddParameters(cmd.Parameters, row.Values.ToList());

                    using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        var outRow = new Dictionary<string, object?>();
                        for (int i = 0; i < reader.FieldCount; i++)
                            outRow[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        result.Add(outRow);
                    }
                }
                progress.Report($"SQL Server Input (行ごと実行): {result.Count}行 読み込み完了");
            }
            else if (!executeEachRow && inputData.Count > 0 && (hasNamedParams || sql.Contains('?')))
            {
                using var cmd = new SqlCommand(execSql, conn);
                if (hasNamedParams)
                    AddNamedParameters(cmd.Parameters, inputData[0]);
                else
                    AddParameters(cmd.Parameters, inputData[0].Values.ToList());

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var outRow = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        outRow[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    result.Add(outRow);
                }
                progress.Report($"SQL Server Input (パラメータ実行): {result.Count}行 読み込み完了");
            }
            else
            {
                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var outRow = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        outRow[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    result.Add(outRow);
                }
                progress.Report($"SQL Server Input: {result.Count}行 読み込み完了");
            }

            return result;
        }

        private static bool HasNamedParams(string sql)
            => Regex.IsMatch(sql, @":\{[a-zA-Z_]\w*\}");

        private static string ReplaceNamedPlaceholders(string sql)
            => Regex.Replace(sql, @":\{([a-zA-Z_]\w*)\}", m => $"@{m.Groups[1].Value}");

        private static string ReplacePositionalPlaceholders(string sql)
        {
            int idx = 0;
            return Regex.Replace(sql, @"\?", _ => $"@p{idx++}");
        }

        private static void AddNamedParameters(SqlParameterCollection p, Dictionary<string, object?> row)
        {
            foreach (var kvp in row)
                p.AddWithValue($"@{kvp.Key}", kvp.Value ?? DBNull.Value);
        }

        private static void AddParameters(SqlParameterCollection p, List<object?> values)
        {
            for (int i = 0; i < values.Count; i++)
                p.AddWithValue($"@p{i}", values[i] ?? DBNull.Value);
        }

        public override string GetDisplayIcon() => "🟦";
    }
}
