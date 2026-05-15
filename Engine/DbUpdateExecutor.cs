using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Models;
using BreezeFlow.Models.Stores;

namespace BreezeFlow.Engine
{
    /// <summary>
    /// DB Update (UPDATE のみ) ステップの共通実装。
    /// </summary>
    public static class DbUpdateExecutor
    {
        public readonly record struct Result(int Processed, int Updated);

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
                progress.Report("DB Update: 入力データなし");
                return new Result(0, 0);
            }
            if (keyFields.Count == 0)
                throw new InvalidOperationException("DB Update: キーフィールドが設定されていません。");

            using var conn = provider.CreateConnection(connectionString);
            await conn.OpenAsync(ct);

            int processed = 0, updated = 0;
            var transaction = await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var allCols = row.Keys.ToList();

                    var updateCols = string.IsNullOrWhiteSpace(updateFieldsRaw)
                        ? allCols.Where(c => !keyFields.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList()
                        : updateFieldsRaw.Split(',')
                                         .Select(f => f.Trim())
                                         .Where(f => f.Length > 0)
                                         .ToList();

                    if (updateCols.Count == 0) continue;

                    var sql = provider.BuildKeyedUpdateSql(tableName, updateCols, keyFields);

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.Transaction = transaction;

                    for (int i = 0; i < updateCols.Count; i++)
                    {
                        var p = cmd.CreateParameter();
                        p.ParameterName = provider.FormatPositionalParamName(i);
                        p.Value = GetValueIgnoreCase(row, updateCols[i]);
                        cmd.Parameters.Add(p);
                    }
                    for (int i = 0; i < keyFields.Count; i++)
                    {
                        var p = cmd.CreateParameter();
                        p.ParameterName = provider.FormatPositionalParamName(updateCols.Count + i);
                        p.Value = GetValueIgnoreCase(row, keyFields[i]);
                        cmd.Parameters.Add(p);
                    }

                    int affected = await cmd.ExecuteNonQueryAsync(ct);
                    processed++;
                    updated += affected;

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

            if (updated == 0 && processed > 0)
            {
                progress.Report(
                    $"⚠ DB Update: 入力 {processed} 行を処理しましたが、DB は 0 行も更新されていません。" +
                    " キー値が DB に存在しない／キー列名の大文字小文字違い／入力行にキー列が無い等の可能性があります。");
            }
            else if (updated < processed)
            {
                progress.Report($"DB Update: 入力 {processed} 行 / DB 更新 {updated} 行 (一部のキーが DB に存在しません)");
            }
            else
            {
                progress.Report($"DB Update: 入力 {processed} 行 / DB 更新 {updated} 行");
            }

            return new Result(processed, updated);
        }

        /// <summary>
        /// 入力行から指定キーの値を大小区別なしで取得する。
        /// 入力カラム名のケース違いで WHERE 句のバインドが空になるのを防ぐ。
        /// </summary>
        private static object GetValueIgnoreCase(Dictionary<string, object?> row, string key)
        {
            if (row.TryGetValue(key, out var v) && v != null) return v;
            foreach (var kv in row)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value ?? DBNull.Value;
            }
            return DBNull.Value;
        }
    }
}
