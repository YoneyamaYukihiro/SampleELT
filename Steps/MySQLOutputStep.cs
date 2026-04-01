using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class MySQLOutputStep : StepBase
    {
        public override StepType StepType => StepType.MySQLOutput;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = ConnectionRegistry.Instance.ResolveConnectionString(Settings);
            var tableName = Settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "";
            var mode = Settings.TryGetValue("Mode", out var m) ? m?.ToString() ?? "INSERT" : "INSERT";

            if (inputData.Count == 0)
            {
                progress.Report("MySQL Output: 入力データなし");
                return inputData;
            }

            using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync(ct);

            using var transaction = await conn.BeginTransactionAsync(ct);
            try
            {
                int inserted = 0;
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();

                    var columns = row.Keys.ToList();
                    var colNames = string.Join(", ", columns.Select(c => $"`{c}`"));
                    var paramNames = string.Join(", ", columns.Select((c, i) => $"@p{i}"));

                    string sql;
                    if (mode == "UPSERT")
                    {
                        var updateClauses = string.Join(", ", columns.Select((c, i) => $"`{c}` = @p{i}"));
                        sql = $"INSERT INTO `{tableName}` ({colNames}) VALUES ({paramNames}) ON DUPLICATE KEY UPDATE {updateClauses}";
                    }
                    else
                    {
                        sql = $"INSERT INTO `{tableName}` ({colNames}) VALUES ({paramNames})";
                    }

                    using var cmd = new MySqlCommand(sql, conn, transaction);

                    for (int i = 0; i < columns.Count; i++)
                    {
                        cmd.Parameters.AddWithValue($"@p{i}", row[columns[i]] ?? DBNull.Value);
                    }

                    await cmd.ExecuteNonQueryAsync(ct);
                    inserted++;
                }

                await transaction.CommitAsync(ct);
                progress.Report($"MySQL Output: {inserted}行 書き込み完了");
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }

            return inputData;
        }

        public override string GetDisplayIcon() => "🐬";
    }
}
