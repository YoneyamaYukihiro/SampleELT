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
            var sql = TrimSql(Settings.TryGetValue("SQL", out var s) ? s?.ToString() ?? "" : "");
            var executeEachRow = Settings.TryGetValue("ExecuteEachRow", out var eer)
                && eer?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            var result = new List<Dictionary<string, object?>>();

            using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync(ct);

            var hasNamedParams = HasNamedParams(sql);

            // バインドパラメータのデバッグ情報を出力
            if (hasNamedParams)
                progress.Report($"Oracle Input: 名前付きパラメータ検出 (:{{}}) → Oracle バインド変数に変換");
            else if (sql.Contains('?'))
                progress.Report($"Oracle Input: ? プレースホルダー検出 → 位置バインド");
            else
                progress.Report("Oracle Input: パラメータなし → そのまま実行");

            if (inputData.Count > 0)
            {
                var fields = string.Join(", ", inputData[0].Select(kv => $"{kv.Key}={kv.Value ?? "NULL"}"));
                progress.Report($"Oracle Input: 入力フィールド [{fields}]");
            }

            if (executeEachRow && inputData.Count > 0)
            {
                // 前ステップの各行の値を順番にパラメータとして渡す（行ごとに実行）
                var oracleSql = hasNamedParams ? ReplaceNamedPlaceholders(sql) : ReplacePlaceholders(sql);
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    using var cmd = new OracleCommand(oracleSql, conn);
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
                progress.Report($"Oracle Input (行ごと実行): {result.Count}行 読み込み完了");
            }
            else if (!executeEachRow && inputData.Count > 0 && (hasNamedParams || sql.Contains('?')))
            {
                // 最初の行の値をパラメータとして使用（lookup パターン）
                var oracleSql = hasNamedParams ? ReplaceNamedPlaceholders(sql) : ReplacePlaceholders(sql);
                progress.Report($"Oracle Input: 実行SQL →\n{oracleSql}");
                using var cmd = new OracleCommand(oracleSql, conn);
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
                progress.Report($"Oracle Input (パラメータ実行): {result.Count}行 読み込み完了");
            }
            else
            {
                progress.Report($"Oracle Input: 実行SQL →\n{sql}");
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

        /// <summary>末尾の空白・セミコロンを除去する（Oracle は末尾セミコロンを許容しない）</summary>
        private static string TrimSql(string sql) => sql.TrimEnd().TrimEnd(';').TrimEnd();

        /// <summary>SQL に :{fieldname} 形式の名前付きプレースホルダーが含まれるか判定する</summary>
        private static bool HasNamedParams(string sql)
            => Regex.IsMatch(sql, @":\{[a-zA-Z_]\w*\}");

        /// <summary>:{fieldname} を Oracle バインド変数 :_fn_fieldname に変換する</summary>
        private static string ReplaceNamedPlaceholders(string sql)
            => Regex.Replace(sql, @":\{([a-zA-Z_]\w*)\}", m => $":_fn_{m.Groups[1].Value}");

        /// <summary>入力行のフィールドを名前でバインドする</summary>
        private static void AddNamedParameters(OracleParameterCollection p, Dictionary<string, object?> row)
        {
            foreach (var kvp in row)
                p.Add(new OracleParameter($":_fn_{kvp.Key}", kvp.Value ?? DBNull.Value));
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
