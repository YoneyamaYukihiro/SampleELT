using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class OracleInputStep : StepBase
    {
        public override StepType StepType => StepType.OracleInput;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = Settings.TryGetValue("ConnectionString", out var cs) ? cs?.ToString() ?? "" : "";
            var sql = Settings.TryGetValue("SQL", out var s) ? s?.ToString() ?? "" : "";

            var result = new List<Dictionary<string, object?>>();

            using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync(ct);

            using var cmd = new OracleCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                result.Add(row);
            }

            progress.Report($"Oracle: {result.Count}行 読み込み完了");
            return result;
        }

        public override string GetDisplayIcon() => "🔶";
    }
}
