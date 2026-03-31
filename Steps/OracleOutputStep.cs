using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class OracleOutputStep : StepBase
    {
        public override StepType StepType => StepType.OracleOutput;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = Settings.TryGetValue("ConnectionString", out var cs) ? cs?.ToString() ?? "" : "";
            var tableName = Settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "";
            var mode = Settings.TryGetValue("Mode", out var m) ? m?.ToString() ?? "INSERT" : "INSERT";

            if (inputData.Count == 0)
            {
                progress.Report("Oracle Output: 入力データなし");
                return inputData;
            }

            using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync(ct);

            using var transaction = conn.BeginTransaction();
            try
            {
                int inserted = 0;
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();

                    var columns = row.Keys.ToList();
                    var colNames = string.Join(", ", columns);
                    var paramNames = string.Join(", ", columns.Select((c, i) => $":p{i}"));

                    string sql;
                    if (mode == "UPSERT")
                    {
                        var updateClauses = string.Join(", ", columns.Select((c, i) => $"{c} = :p{i}"));
                        sql = $"MERGE INTO {tableName} t USING (SELECT {string.Join(", ", columns.Select((c, i) => $":p{i} AS {c}"))} FROM DUAL) s ON (t.{columns[0]} = s.{columns[0]}) WHEN MATCHED THEN UPDATE SET {updateClauses} WHEN NOT MATCHED THEN INSERT ({colNames}) VALUES ({paramNames})";
                    }
                    else
                    {
                        sql = $"INSERT INTO {tableName} ({colNames}) VALUES ({paramNames})";
                    }

                    using var cmd = new OracleCommand(sql, conn);
                    cmd.Transaction = transaction;

                    for (int i = 0; i < columns.Count; i++)
                    {
                        cmd.Parameters.Add(new OracleParameter($":p{i}", row[columns[i]] ?? DBNull.Value));
                    }

                    await cmd.ExecuteNonQueryAsync(ct);
                    inserted++;
                }

                await transaction.CommitAsync(ct);
                progress.Report($"Oracle Output: {inserted}行 書き込み完了");
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }

            return inputData;
        }

        public override string GetDisplayIcon() => "🔶";
    }
}
