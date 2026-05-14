using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;
using SampleELT.Models.Stores;

namespace SampleELT.Engine
{
    /// <summary>
    /// DB Input ステップの共通実装。プロバイダ越しに ADO.NET にアクセスする。
    /// 行単位ストリーミング (DbDataReader を yield) を提供し、巨大な結果セットでもメモリを消費しない。
    /// </summary>
    public static class DbInputExecutor
    {
        private static readonly Regex NamedParamRegex = new(@":\{([a-zA-Z_]\w*)\}", RegexOptions.Compiled);
        private static readonly Regex PositionalRegex = new(@"\?", RegexOptions.Compiled);

        /// <summary>
        /// レガシー互換: <see cref="ExecuteStreamingAsync"/> をドレインして List にまとめて返す。
        /// テストや非ストリーミング呼び出し元のために残す。新規コードは streaming 版を使うこと。
        /// </summary>
        public static async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            DbProvider provider,
            Dictionary<string, object?> settings,
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var result = new List<Dictionary<string, object?>>();
            await foreach (var row in ExecuteStreamingAsync(provider, settings, ListAsAsync(inputData), progress, ct)
                .WithCancellation(ct).ConfigureAwait(false))
            {
                result.Add(row);
            }
            return result;
        }

        private static async IAsyncEnumerable<Dictionary<string, object?>> ListAsAsync(
            List<Dictionary<string, object?>> list)
        {
            foreach (var row in list) yield return row;
            await Task.CompletedTask;
        }

        /// <summary>
        /// 行単位ストリーミング版。
        /// 入力 (`inputAsync`) を最大 1 回列挙する。`ExecuteEachRow=true` の場合のみ全行に対して
        /// 個別にクエリを発行し、それ以外はパラメータ用の最初の行だけを読む。
        /// </summary>
        public static async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            DbProvider provider,
            Dictionary<string, object?> settings,
            IAsyncEnumerable<Dictionary<string, object?>> inputAsync,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
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

            using var conn = provider.CreateConnection(connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            long totalRows = 0;

            if (executeEachRow)
            {
                // 行ごとに SQL を実行。入力行を 1 行ずつ消費しながらクエリを発行 → 結果を yield。
                bool anyInput = false;
                await foreach (var row in inputAsync.WithCancellation(ct).ConfigureAwait(false))
                {
                    anyInput = true;
                    ct.ThrowIfCancellationRequested();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = execSql;
                    if (hasNamedParams) AddNamedParameters(cmd, row, provider);
                    else                AddPositionalParameters(cmd, row.Values.ToList(), provider);

                    await foreach (var outRow in ReadRowsAsync(cmd, ct).ConfigureAwait(false))
                    {
                        totalRows++;
                        yield return outRow;
                    }
                }
                if (!anyInput)
                {
                    // 入力が空ならフォールバックで通常実行
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    await foreach (var outRow in ReadRowsAsync(cmd, ct).ConfigureAwait(false))
                    {
                        totalRows++;
                        yield return outRow;
                    }
                }
                progress.Report($"{provider.LogPrefix} Input (行ごと実行): {totalRows}行 読み込み完了");
            }
            else if (hasNamedParams || hasPositional)
            {
                // パラメータあり: 入力の最初の行をパラメータに使って 1 回だけ実行
                Dictionary<string, object?>? firstRow = null;
                await foreach (var row in inputAsync.WithCancellation(ct).ConfigureAwait(false))
                {
                    firstRow = row;
                    break;
                }

                using var cmd = conn.CreateCommand();
                cmd.CommandText = execSql;
                if (firstRow != null)
                {
                    if (hasNamedParams) AddNamedParameters(cmd, firstRow, provider);
                    else                AddPositionalParameters(cmd, firstRow.Values.ToList(), provider);
                }
                await foreach (var outRow in ReadRowsAsync(cmd, ct).ConfigureAwait(false))
                {
                    totalRows++;
                    yield return outRow;
                }
                progress.Report($"{provider.LogPrefix} Input (パラメータ実行): {totalRows}行 読み込み完了");
            }
            else
            {
                // パラメータなし: 入力は無視して 1 回だけ実行
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await foreach (var outRow in ReadRowsAsync(cmd, ct).ConfigureAwait(false))
                {
                    totalRows++;
                    yield return outRow;
                }
                progress.Report($"{provider.LogPrefix} Input: {totalRows}行 読み込み完了");
            }
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

        private static async IAsyncEnumerable<Dictionary<string, object?>> ReadRowsAsync(
            DbCommand cmd,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var row = new Dictionary<string, object?>(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                yield return row;
            }
        }
    }
}
