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

            // SQL に @変数 が含まれる場合に備えて AllowUserVariables を有効化
            var csb = new MySqlConnectionStringBuilder(connectionString)
            {
                AllowUserVariables = true
            };
            connectionString = csb.ConnectionString;

            var sql = Settings.TryGetValue("SQL", out var s) ? s?.ToString() ?? "" : "";
            var executeEachRow = Settings.TryGetValue("ExecuteEachRow", out var eer)
                && eer?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            var result = new List<Dictionary<string, object?>>();

            using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var hasNamedParams = HasNamedParams(sql);
            var execSql = hasNamedParams ? ReplaceNamedPlaceholders(sql) : sql;

            if (executeEachRow && inputData.Count > 0)
            {
                // 前ステップの各行の値を順番にパラメータとして渡す（行ごとに実行）
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    using var cmd = new MySqlCommand(execSql, conn);
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
                progress.Report($"MySQL Input (行ごと実行): {result.Count}行 読み込み完了");
            }
            else if (!executeEachRow && inputData.Count > 0 && (hasNamedParams || sql.Contains('?')))
            {
                // 最初の行の値をパラメータとして使用（lookup パターン）
                using var cmd = new MySqlCommand(execSql, conn);
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
                progress.Report($"MySQL Input (パラメータ実行): {result.Count}行 読み込み完了");
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

        /// <summary>SQL に :{fieldname} 形式の名前付きプレースホルダーが含まれるか判定する</summary>
        private static bool HasNamedParams(string sql)
            => System.Text.RegularExpressions.Regex.IsMatch(sql, @":\{[a-zA-Z_]\w*\}");

        /// <summary>:{fieldname} を MySQL バインド変数 @fieldname に変換する</summary>
        private static string ReplaceNamedPlaceholders(string sql)
            => System.Text.RegularExpressions.Regex.Replace(
                sql, @":\{([a-zA-Z_]\w*)\}", m => $"@{m.Groups[1].Value}");

        /// <summary>入力行のフィールドを名前でバインドする</summary>
        private static void AddNamedParameters(MySqlParameterCollection p, Dictionary<string, object?> row)
        {
            foreach (var kvp in row)
                p.AddWithValue($"@{kvp.Key}", kvp.Value ?? DBNull.Value);
        }

        private static void AddParameters(MySqlParameterCollection p, List<object?> values)
        {
            for (int i = 0; i < values.Count; i++)
                p.AddWithValue($"@p{i}", values[i] ?? DBNull.Value);
        }

        public override string GetDisplayIcon() => "🐬";
    }
}
