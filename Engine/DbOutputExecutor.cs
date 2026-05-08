using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;
using SampleELT.Models.Stores;

namespace SampleELT.Engine
{
    /// <summary>
    /// DB Output ステップの共通実装。プロバイダ越しに INSERT/UPSERT を発行する。
    /// </summary>
    public static class DbOutputExecutor
    {
        public static async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            DbProvider provider,
            Dictionary<string, object?> settings,
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = provider.PreprocessConnectionString(
                IConnectionStore.Default.ResolveConnectionString(settings));
            var tableName = settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "";
            var mode      = settings.TryGetValue("Mode", out var m) ? m?.ToString() ?? "INSERT" : "INSERT";
            int commitSize = settings.TryGetValue("CommitSize", out var cs)
                && int.TryParse(cs?.ToString(), out var csVal) && csVal > 0 ? csVal : 100;

            if (inputData.Count == 0)
            {
                progress.Report($"{provider.LogPrefix} Output: 入力データなし");
                return inputData;
            }

            using var conn = provider.CreateConnection(connectionString);
            await conn.OpenAsync(ct);

            int written = 0;
            var transaction = await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();

                    var columns = row.Keys.ToList();
                    var sql = mode == "UPSERT"
                        ? provider.BuildUpsertSql(tableName, columns)
                        : provider.BuildInsertSql(tableName, columns);

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.Transaction = transaction;
                    for (int i = 0; i < columns.Count; i++)
                    {
                        var p = cmd.CreateParameter();
                        p.ParameterName = provider.FormatPositionalParamName(i);
                        p.Value = row[columns[i]] ?? DBNull.Value;
                        cmd.Parameters.Add(p);
                    }

                    await cmd.ExecuteNonQueryAsync(ct);
                    written++;

                    if (written % commitSize == 0)
                    {
                        await transaction.CommitAsync(ct);
                        await transaction.DisposeAsync();
                        transaction = await conn.BeginTransactionAsync(ct);
                    }
                }

                await transaction.CommitAsync(ct);
                progress.Report($"{provider.LogPrefix} Output: {written}行 書き込み完了");
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
    }
}
