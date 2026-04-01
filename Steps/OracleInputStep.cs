using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
            var connectionString = ConnectionRegistry.Instance.ResolveConnectionString(Settings);
            var sql = Settings.TryGetValue("SQL", out var s) ? s?.ToString() ?? "" : "";
            var executeEachRow = Settings.TryGetValue("ExecuteEachRow", out var eer)
                && eer?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            var result = new List<Dictionary<string, object?>>();

            using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync(ct);

            if (executeEachRow && inputData.Count > 0)
            {
                // 前ステップの各行の値を順番にパラメータとして渡す（行ごとに実行）
                var oracleSql = ReplacePlaceholders(sql);
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    using var cmd = new OracleCommand(oracleSql, conn);
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
                progress.Report($"Oracle Input (行ごと実行): {result.Count}行 読み込み完了");
            }
            else if (!executeEachRow && inputData.Count > 0 && sql.Contains('?'))
            {
                // 最初の行の値を ? パラメータとして使用（lookup パターン）
                var oracleSql = ReplacePlaceholders(sql);
                using var cmd = new OracleCommand(oracleSql, conn);
                AddParameters(cmd.Parameters, inputData[0].Values.ToList());

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var outRow = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        outRow[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    result.Add(outRow);
                }
                progress.Report($"Oracle Input (パラメータ実行): {result.Count}行 読み込み完了");
            }
            else
            {
                using var cmd = new OracleCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var outRow = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        outRow[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    result.Add(outRow);
                }
                progress.Report($"Oracle Input: {result.Count}行 読み込み完了");
            }

            return result;
        }

        /// <summary>Oracle は ? をサポートしないため :_p0, :_p1, ... に変換する</summary>
        private static string ReplacePlaceholders(string sql)
        {
            int idx = 0;
            return Regex.Replace(sql, @"\?", _ => $":_p{idx++}");
        }

        private static void AddParameters(OracleParameterCollection p, List<object?> values)
        {
            for (int i = 0; i < values.Count; i++)
                p.Add(new OracleParameter($":_p{i}", values[i] ?? DBNull.Value));
        }

        public override string GetDisplayIcon() => "🔶";
    }
}
