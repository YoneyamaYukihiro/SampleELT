using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;
using SampleELT.Models.Stores;

namespace SampleELT.Engine
{
    /// <summary>
    /// DB Input ステップの共通実装。プロバイダ越しに ADO.NET にアクセスする。
    /// </summary>
    public static class DbInputExecutor
    {
        private static readonly Regex NamedParamRegex = new(@":\{([a-zA-Z_]\w*)\}", RegexOptions.Compiled);
        private static readonly Regex PositionalRegex = new(@"\?", RegexOptions.Compiled);

        public static async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            DbProvider provider,
            Dictionary<string, object?> settings,
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = provider.PreprocessConnectionString(
                IConnectionStore.Default.ResolveConnectionString(settings));
            var sql = provider.PreprocessSql(
                settings.TryGetValue("SQL", out var s) ? s?.ToString() ?? "" : "");
            var executeEachRow = settings.TryGetValue("ExecuteEachRow", out var eer)
                && eer?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            var hasNamedParams = NamedParamRegex.IsMatch(sql);
            var hasPositional  = !hasNamedParams && sql.Contains('?');
            var execSql = hasNamedParams ? RewriteNamedParams(sql, provider)
                        : hasPositional  ? RewritePositionalParams(sql, provider)
                        : sql;

            var result = new List<Dictionary<string, object?>>();

            using var conn = provider.CreateConnection(connectionString);
            await conn.OpenAsync(ct);

            if (executeEachRow && inputData.Count > 0)
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = execSql;
                    if (hasNamedParams)
                        AddNamedParameters(cmd, row, provider);
                    else
                        AddPositionalParameters(cmd, row.Values.ToList(), provider);

                    await ReadIntoAsync(cmd, result, ct);
                }
                progress.Report($"{provider.LogPrefix} Input (行ごと実行): {result.Count}行 読み込み完了");
            }
            else if (!executeEachRow && inputData.Count > 0 && (hasNamedParams || hasPositional))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = execSql;
                if (hasNamedParams)
                    AddNamedParameters(cmd, inputData[0], provider);
                else
                    AddPositionalParameters(cmd, inputData[0].Values.ToList(), provider);

                await ReadIntoAsync(cmd, result, ct);
                progress.Report($"{provider.LogPrefix} Input (パラメータ実行): {result.Count}行 読み込み完了");
            }
            else
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await ReadIntoAsync(cmd, result, ct);
                progress.Report($"{provider.LogPrefix} Input: {result.Count}行 読み込み完了");
            }

            return result;
        }

        private static string RewriteNamedParams(string sql, DbProvider provider)
            => NamedParamRegex.Replace(sql, m => provider.FormatNamedSqlPlaceholder(m.Groups[1].Value));

        private static string RewritePositionalParams(string sql, DbProvider provider)
        {
            int idx = 0;
            return PositionalRegex.Replace(sql, _ => provider.FormatPositionalSqlPlaceholder(idx++));
        }

        private static void AddNamedParameters(DbCommand cmd, Dictionary<string, object?> row, DbProvider provider)
        {
            foreach (var kv in row)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = provider.FormatNamedParamName(kv.Key);
                p.Value = kv.Value ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }

        private static void AddPositionalParameters(DbCommand cmd, List<object?> values, DbProvider provider)
        {
            for (int i = 0; i < values.Count; i++)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = provider.FormatPositionalParamName(i);
                p.Value = values[i] ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }

        private static async Task ReadIntoAsync(
            DbCommand cmd,
            List<Dictionary<string, object?>> dest,
            CancellationToken ct)
        {
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                dest.Add(row);
            }
        }
    }
}
