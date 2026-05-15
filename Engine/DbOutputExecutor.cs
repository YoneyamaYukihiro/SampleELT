using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Models;
using BreezeFlow.Models.Stores;

namespace BreezeFlow.Engine
{
    /// <summary>
    /// DB Output ステップの共通実装。プロバイダ越しに INSERT/UPSERT を発行する。
    /// 行単位ストリーミング: 入力を一行ずつ書き込み、そのまま下流へ流す (passthrough)。
    /// CommitSize ごとに COMMIT を区切る。
    /// </summary>
    public static class DbOutputExecutor
    {
        /// <summary>
        /// ストリーミング版。入力 IAsyncEnumerable を 1 行ずつ消費し、行ごとに INSERT/UPSERT を実行、
        /// 書き込んだ行をそのまま下流に yield する。CommitSize ごとに COMMIT 区切り。
        /// </summary>
        public static async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            DbProvider provider,
            Dictionary<string, object?> settings,
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var connectionString = provider.PreprocessConnectionString(
                IConnectionStore.Default.ResolveConnectionString(settings));
            var tableName = settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "";
            var mode      = settings.TryGetValue("Mode", out var m) ? m?.ToString() ?? "INSERT" : "INSERT";
            int commitSize = settings.TryGetValue("CommitSize", out var cs)
                && int.TryParse(cs?.ToString(), out var csVal) && csVal > 0 ? csVal : 100;

            using var conn = provider.CreateConnection(connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            int written = 0;
            DbTransaction? transaction = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            bool committed = false;
            try
            {
                await foreach (var row in input.WithCancellation(ct).ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();

                    var columns = row.Keys.ToList();
                    var sql = mode == "UPSERT"
                        ? provider.BuildUpsertSql(tableName, columns)
                        : provider.BuildInsertSql(tableName, columns);

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        cmd.Transaction = transaction;
                        for (int i = 0; i < columns.Count; i++)
                        {
                            var p = cmd.CreateParameter();
                            p.ParameterName = provider.FormatPositionalParamName(i);
                            p.Value = row[columns[i]] ?? DBNull.Value;
                            cmd.Parameters.Add(p);
                        }
                        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    written++;
                    if (written % commitSize == 0)
                    {
                        await transaction!.CommitAsync(ct).ConfigureAwait(false);
                        await transaction.DisposeAsync().ConfigureAwait(false);
                        transaction = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
                    }

                    yield return row;
                }

                if (transaction != null)
                {
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                    committed = true;
                }
                if (written == 0)
                    progress.Report($"{provider.LogPrefix} Output: 入力データなし");
                else
                    progress.Report($"{provider.LogPrefix} Output: {written}行 書き込み完了");
            }
            finally
            {
                if (transaction != null)
                {
                    if (!committed)
                    {
                        try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                        catch { /* best effort */ }
                    }
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>レガシー互換 List API。テストや非ストリーミング呼び出し元向け。</summary>
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
    }
}
