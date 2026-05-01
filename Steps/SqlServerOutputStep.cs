using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class SqlServerOutputStep : StepBase
    {
        public override StepType StepType => StepType.DBOutput;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = ConnectionRegistry.Instance.ResolveConnectionString(Settings);
            var tableName = Settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "";
            var mode = Settings.TryGetValue("Mode", out var m) ? m?.ToString() ?? "INSERT" : "INSERT";
            int commitSize = Settings.TryGetValue("CommitSize", out var cs)
                && int.TryParse(cs?.ToString(), out var csVal) && csVal > 0 ? csVal : 100;

            if (inputData.Count == 0)
            {
                progress.Report("SQL Server Output: 入力データなし");
                return inputData;
            }

            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            int inserted = 0;
            var transaction = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();

                    var columns = row.Keys.ToList();
                    var colNames = string.Join(", ", columns.Select(c => $"[{c}]"));
                    var paramNames = string.Join(", ", columns.Select((c, i) => $"@p{i}"));

                    string sql;
                    if (mode == "UPSERT")
                    {
                        var keyCol = columns[0];
                        var srcCols = string.Join(", ", columns.Select((c, i) => $"@p{i} AS [{c}]"));
                        var updateClauses = string.Join(", ",
                            columns.Where(c => c != keyCol).Select(c => $"t.[{c}] = s.[{c}]"));
                        sql = $"MERGE INTO [{tableName}] AS t USING (SELECT {srcCols}) AS s " +
                              $"ON t.[{keyCol}] = s.[{keyCol}] " +
                              (updateClauses.Length > 0 ? $"WHEN MATCHED THEN UPDATE SET {updateClauses} " : "") +
                              $"WHEN NOT MATCHED THEN INSERT ({colNames}) VALUES ({string.Join(", ", columns.Select(c => $"s.[{c}]"))});";
                    }
                    else
                    {
                        sql = $"INSERT INTO [{tableName}] ({colNames}) VALUES ({paramNames})";
                    }

                    using var cmd = new SqlCommand(sql, conn, transaction);
                    for (int i = 0; i < columns.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{i}", row[columns[i]] ?? DBNull.Value);

                    await cmd.ExecuteNonQueryAsync(ct);
                    inserted++;

                    if (inserted % commitSize == 0)
                    {
                        await transaction.CommitAsync(ct);
                        await transaction.DisposeAsync();
                        transaction = (SqlTransaction)await conn.BeginTransactionAsync(ct);
                    }
                }

                await transaction.CommitAsync(ct);
                progress.Report($"SQL Server Output: {inserted}行 書き込み完了");
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
            finally
            {
                await transaction.DisposeAsync();
            }

            return inputData;
        }

        public override string GetDisplayIcon() => "🟦";
    }
}
