using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class MySQLInputStep : StepBase
    {
        public override StepType StepType => StepType.MySQLInput;

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

            using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync(ct);

            if (executeEachRow && inputData.Count > 0)
            {
                // 前ステップの各行の値を順番に ? パラメータとして渡す
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    using var cmd = new MySqlCommand(sql, conn);
                    var values = row.Values.ToList();
                    for (int i = 0; i < values.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{i}", values[i] ?? DBNull.Value);

                    using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        var outRow = new Dictionary<string, object?>();
                        for (int i = 0; i < reader.FieldCount; i++)
                            outRow[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        result.Add(outRow);
                    }
                }
                progress.Report($"MySQL Input (行ごと実行): {result.Count}行 読み込み完了");
            }
            else
            {
                using var cmd = new MySqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var outRow = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        outRow[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    result.Add(outRow);
                }
                progress.Report($"MySQL Input: {result.Count}行 読み込み完了");
            }

            return result;
        }

        public override string GetDisplayIcon() => "🐬";
    }
}
