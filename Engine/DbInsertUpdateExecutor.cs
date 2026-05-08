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
    /// Insert/Update (UPSERT) ステップの共通実装。プロバイダ越しに各 DB の UPSERT 構文を発行する。
    /// </summary>
    public static class DbInsertUpdateExecutor
    {
        public readonly record struct Result(int Processed, int Inserted, int Updated, int Unchanged);

        public static async Task<Result> ExecuteAsync(
            DbProvider provider,
            Dictionary<string, object?> settings,
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = provider.PreprocessConnectionString(
                IConnectionStore.Default.ResolveConnectionString(settings));
            var tableName = settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "";
            var keyFieldsRaw = settings.TryGetValue("KeyFields", out var kf) ? kf?.ToString() ?? "" : "";
            var updateFieldsRaw = settings.TryGetValue("UpdateFields", out var uf) ? uf?.ToString() ?? "" : "";
            int commitSize = settings.TryGetValue("CommitSize", out var cs)
                && int.TryParse(cs?.ToString(), out var csVal) && csVal > 0 ? csVal : 100;

            var keyFields = keyFieldsRaw.Split(',')
                .Select(f => f.Trim())
                .Where(f => f.Length > 0)
                .ToList();

            if (inputData.Count == 0)
            {
                progress.Report("Insert/Update: 入力データなし");
                return new Result(0, 0, 0, 0);
            }
            if (keyFields.Count == 0)
                throw new InvalidOperationException("Insert/Update: キーフィールドが設定されていません。");

            using var conn = provider.CreateConnection(connectionString);
            await conn.OpenAsync(ct);

            int processed = 0, inserted = 0, updated = 0, unchanged = 0;
            var transaction = await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var (allCols, updateCols) = ResolveColumns(row, keyFields, updateFieldsRaw);

                    var sql = provider.BuildKeyedUpsertSql(tableName, allCols, keyFields, updateCols);

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.Transaction = transaction;
                    for (int i = 0; i < allCols.Count; i++)
                    {
                        var p = cmd.CreateParameter();
                        p.ParameterName = provider.FormatPositionalParamName(i);
                        p.Value = row[allCols[i]] ?? DBNull.Value;
                        cmd.Parameters.Add(p);
                    }

                    int rc = await cmd.ExecuteNonQueryAsync(ct);
                    processed++;

                    if (provider.ProvidesInsertUpdateBreakdown)
                    {
                        // MySQL ON DUPLICATE KEY UPDATE: 1=insert, 2=update, 0=update対象だが値同一
                        if (rc == 1) inserted++;
                        else if (rc == 2) updated++;
                        else unchanged++;
                    }
                    else
                    {
                        // 他 DB は内訳が取れないため、影響行数の合計だけを inserted に蓄積
                        inserted += rc;
                    }

                    if (processed % commitSize == 0)
                    {
                        await transaction.CommitAsync(ct);
                        await transaction.DisposeAsync();
                        transaction = await conn.BeginTransactionAsync(ct);
                    }
                }
                await transaction.CommitAsync(ct);
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

            if (provider.ProvidesInsertUpdateBreakdown)
            {
                var msg = $"Insert/Update: 入力 {processed} 行 / DB Insert {inserted} 行 + Update {updated} 行";
                if (unchanged > 0) msg += $" (変更なし {unchanged} 行)";
                progress.Report(msg);
            }
            else
            {
                progress.Report($"Insert/Update: 入力 {processed} 行 / DB 影響 {inserted} 行");
            }

            return new Result(processed, inserted, updated, unchanged);
        }

        /// <summary>
        /// 入力行から allCols と updateCols を解決する。UpdateFields が明示されている場合は
        /// テーブルに存在しない入力カラムを除外する。
        /// </summary>
        private static (List<string> allCols, List<string> updateCols) ResolveColumns(
            Dictionary<string, object?> row,
            List<string> keyFields,
            string updateFieldsRaw)
        {
            var allCols = row.Keys.ToList();

            List<string> updateCols;
            if (string.IsNullOrWhiteSpace(updateFieldsRaw))
            {
                updateCols = allCols
                    .Where(c => !keyFields.Contains(c, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                updateCols = updateFieldsRaw.Split(',')
                    .Select(f => f.Trim())
                    .Where(f => f.Length > 0)
                    .Where(f => allCols.Any(c => string.Equals(c, f, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                // テーブルに無いカラムを INSERT 部から除外
                var allowed = new HashSet<string>(
                    keyFields.Concat(updateCols), StringComparer.OrdinalIgnoreCase);
                allCols = allCols.Where(c => allowed.Contains(c)).ToList();
            }

            return (allCols, updateCols);
        }
    }
}
