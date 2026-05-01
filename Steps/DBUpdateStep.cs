using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using SampleELT.Models;

namespace SampleELT.Steps
{
    /// <summary>
    /// 入力データのキーで検索し、一致した場合のみ UPDATE するステップ（INSERT しない）。
    /// Settings["ConnectionId"]  : 接続ID
    /// Settings["TableName"]     : 対象テーブル名
    /// Settings["KeyFields"]     : カンマ区切りのキーフィールド名
    /// Settings["UpdateFields"]  : カンマ区切りの更新フィールド名 (空の場合はキー以外の全フィールド)
    /// </summary>
    public class DBUpdateStep : StepBase
    {
        public override StepType StepType => StepType.DBUpdate;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = ConnectionRegistry.Instance.ResolveConnectionString(Settings);
            var tableName = Settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "";
            var keyFieldsRaw = Settings.TryGetValue("KeyFields", out var kf) ? kf?.ToString() ?? "" : "";
            var updateFieldsRaw = Settings.TryGetValue("UpdateFields", out var uf) ? uf?.ToString() ?? "" : "";

            var keyFields = keyFieldsRaw.Split(',').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList();

            if (inputData.Count == 0)
            {
                progress.Report("DB Update: 入力データなし");
                return inputData;
            }

            if (keyFields.Count == 0)
                throw new InvalidOperationException("DB Update: キーフィールドが設定されていません。");

            int commitSize = Settings.TryGetValue("CommitSize", out var cs)
                && int.TryParse(cs?.ToString(), out var csVal) && csVal > 0 ? csVal : 100;

            var conn = ConnectionRegistry.Instance.FindConnection(Settings);
            var dbType = conn?.DbType ?? DbType.MySQL;

            var (processed, updated) = dbType switch
            {
                DbType.Oracle     => await ExecuteOracleAsync(connectionString, tableName, keyFields, updateFieldsRaw, inputData, commitSize, ct),
                DbType.PostgreSQL => await ExecutePostgreSQLAsync(connectionString, tableName, keyFields, updateFieldsRaw, inputData, commitSize, ct),
                DbType.SqlServer  => await ExecuteSqlServerAsync(connectionString, tableName, keyFields, updateFieldsRaw, inputData, commitSize, ct),
                DbType.Sqlite     => await ExecuteSqliteAsync(connectionString, tableName, keyFields, updateFieldsRaw, inputData, commitSize, ct),
                // MariaDB は MySQL と同じ
                _                 => await ExecuteMySQLAsync(connectionString, tableName, keyFields, updateFieldsRaw, inputData, commitSize, ct)
            };

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
            return inputData;
        }

        /// <summary>
        /// 入力行から指定キーの値を大小区別なしで取得する。
        /// DB Input が返すカラム名のケースが期待と異なっていても WHERE 句のバインドが空にならないようにする。
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

        private static async Task<(int processed, int updated)> ExecuteOracleAsync(
            string connectionString, string tableName,
            List<string> keyFields, string updateFieldsRaw,
            List<Dictionary<string, object?>> inputData, int commitSize, CancellationToken ct)
        {
            using var dbConn = new OracleConnection(connectionString);
            await dbConn.OpenAsync(ct);
            int processed = 0;
            int updated   = 0;
            var transaction = dbConn.BeginTransaction();
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var allCols = row.Keys.ToList();

                    var updateCols = string.IsNullOrWhiteSpace(updateFieldsRaw)
                        ? allCols.Where(c => !keyFields.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList()
                        : updateFieldsRaw.Split(',').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList();

                    if (updateCols.Count == 0) continue;

                    var setClauses = updateCols.Select((c, i) => $"{c} = :p{i}").ToList();
                    var whereClauses = keyFields.Select((k, i) => $"{k} = :p{updateCols.Count + i}").ToList();
                    var sql = $"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

                    using var cmd = new OracleCommand(sql, dbConn);
                    cmd.Transaction = transaction;
                    for (int i = 0; i < updateCols.Count; i++)
                        cmd.Parameters.Add(new OracleParameter($":p{i}", GetValueIgnoreCase(row, updateCols[i])));
                    for (int i = 0; i < keyFields.Count; i++)
                        cmd.Parameters.Add(new OracleParameter($":p{updateCols.Count + i}", GetValueIgnoreCase(row, keyFields[i])));

                    int affected = await cmd.ExecuteNonQueryAsync(ct);
                    processed++;
                    updated += affected;

                    if (processed % commitSize == 0)
                    {
                        await transaction.CommitAsync(ct);
                        transaction.Dispose();
                        transaction = dbConn.BeginTransaction();
                    }
                }
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
            finally { transaction.Dispose(); }
            return (processed, updated);
        }

        private static async Task<(int processed, int updated)> ExecuteMySQLAsync(
            string connectionString, string tableName,
            List<string> keyFields, string updateFieldsRaw,
            List<Dictionary<string, object?>> inputData, int commitSize, CancellationToken ct)
        {
            using var dbConn = new MySqlConnection(connectionString);
            await dbConn.OpenAsync(ct);
            int processed = 0;
            int updated   = 0;
            var transaction = await dbConn.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var allCols = row.Keys.ToList();

                    var updateCols = string.IsNullOrWhiteSpace(updateFieldsRaw)
                        ? allCols.Where(c => !keyFields.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList()
                        : updateFieldsRaw.Split(',').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList();

                    if (updateCols.Count == 0) continue;

                    var setClauses = updateCols.Select((c, i) => $"{c} = @p{i}").ToList();
                    var whereClauses = keyFields.Select((k, i) => $"{k} = @p{updateCols.Count + i}").ToList();
                    var sql = $"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

                    using var cmd = new MySqlCommand(sql, dbConn, transaction);
                    for (int i = 0; i < updateCols.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{i}", GetValueIgnoreCase(row, updateCols[i]));
                    for (int i = 0; i < keyFields.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{updateCols.Count + i}", GetValueIgnoreCase(row, keyFields[i]));

                    int affected = await cmd.ExecuteNonQueryAsync(ct);
                    processed++;
                    updated += affected;

                    if (processed % commitSize == 0)
                    {
                        await transaction.CommitAsync(ct);
                        await transaction.DisposeAsync();
                        transaction = await dbConn.BeginTransactionAsync(ct);
                    }
                }
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
            finally { await transaction.DisposeAsync(); }
            return (processed, updated);
        }

        private static async Task<(int processed, int updated)> ExecutePostgreSQLAsync(
            string connectionString, string tableName,
            List<string> keyFields, string updateFieldsRaw,
            List<Dictionary<string, object?>> inputData, int commitSize, CancellationToken ct)
        {
            using var dbConn = new NpgsqlConnection(connectionString);
            await dbConn.OpenAsync(ct);
            int processed = 0;
            int updated   = 0;
            var transaction = await dbConn.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var allCols = row.Keys.ToList();

                    var updateCols = string.IsNullOrWhiteSpace(updateFieldsRaw)
                        ? allCols.Where(c => !keyFields.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList()
                        : updateFieldsRaw.Split(',').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList();

                    if (updateCols.Count == 0) continue;

                    var setClauses = updateCols.Select((c, i) => $"\"{c}\" = @p{i}").ToList();
                    var whereClauses = keyFields.Select((k, i) => $"\"{k}\" = @p{updateCols.Count + i}").ToList();
                    var sql = $"UPDATE \"{tableName}\" SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

                    using var cmd = new NpgsqlCommand(sql, dbConn, transaction);
                    for (int i = 0; i < updateCols.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{i}", GetValueIgnoreCase(row, updateCols[i]));
                    for (int i = 0; i < keyFields.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{updateCols.Count + i}", GetValueIgnoreCase(row, keyFields[i]));

                    int affected = await cmd.ExecuteNonQueryAsync(ct);
                    processed++;
                    updated += affected;

                    if (processed % commitSize == 0)
                    {
                        await transaction.CommitAsync(ct);
                        await transaction.DisposeAsync();
                        transaction = await dbConn.BeginTransactionAsync(ct);
                    }
                }
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
            finally { await transaction.DisposeAsync(); }
            return (processed, updated);
        }

        private static async Task<(int processed, int updated)> ExecuteSqlServerAsync(
            string connectionString, string tableName,
            List<string> keyFields, string updateFieldsRaw,
            List<Dictionary<string, object?>> inputData, int commitSize, CancellationToken ct)
        {
            using var dbConn = new SqlConnection(connectionString);
            await dbConn.OpenAsync(ct);
            int processed = 0;
            int updated   = 0;
            var transaction = (SqlTransaction)await dbConn.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var allCols = row.Keys.ToList();

                    var updateCols = string.IsNullOrWhiteSpace(updateFieldsRaw)
                        ? allCols.Where(c => !keyFields.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList()
                        : updateFieldsRaw.Split(',').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList();

                    if (updateCols.Count == 0) continue;

                    var setClauses = updateCols.Select((c, i) => $"[{c}] = @p{i}").ToList();
                    var whereClauses = keyFields.Select((k, i) => $"[{k}] = @p{updateCols.Count + i}").ToList();
                    var sql = $"UPDATE [{tableName}] SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

                    using var cmd = new SqlCommand(sql, dbConn, transaction);
                    for (int i = 0; i < updateCols.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{i}", GetValueIgnoreCase(row, updateCols[i]));
                    for (int i = 0; i < keyFields.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{updateCols.Count + i}", GetValueIgnoreCase(row, keyFields[i]));

                    int affected = await cmd.ExecuteNonQueryAsync(ct);
                    processed++;
                    updated += affected;

                    if (processed % commitSize == 0)
                    {
                        await transaction.CommitAsync(ct);
                        await transaction.DisposeAsync();
                        transaction = (SqlTransaction)await dbConn.BeginTransactionAsync(ct);
                    }
                }
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
            finally { await transaction.DisposeAsync(); }
            return (processed, updated);
        }

        private static async Task<(int processed, int updated)> ExecuteSqliteAsync(
            string connectionString, string tableName,
            List<string> keyFields, string updateFieldsRaw,
            List<Dictionary<string, object?>> inputData, int commitSize, CancellationToken ct)
        {
            using var dbConn = new SqliteConnection(connectionString);
            await dbConn.OpenAsync(ct);
            int processed = 0;
            int updated   = 0;
            var transaction = (SqliteTransaction)await dbConn.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var allCols = row.Keys.ToList();

                    var updateCols = string.IsNullOrWhiteSpace(updateFieldsRaw)
                        ? allCols.Where(c => !keyFields.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList()
                        : updateFieldsRaw.Split(',').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList();

                    if (updateCols.Count == 0) continue;

                    var setClauses = updateCols.Select((c, i) => $"\"{c}\" = @p{i}").ToList();
                    var whereClauses = keyFields.Select((k, i) => $"\"{k}\" = @p{updateCols.Count + i}").ToList();
                    var sql = $"UPDATE \"{tableName}\" SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

                    using var cmd = new SqliteCommand(sql, dbConn, transaction);
                    for (int i = 0; i < updateCols.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{i}", GetValueIgnoreCase(row, updateCols[i]));
                    for (int i = 0; i < keyFields.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{updateCols.Count + i}", GetValueIgnoreCase(row, keyFields[i]));

                    int affected = await cmd.ExecuteNonQueryAsync(ct);
                    processed++;
                    updated += affected;

                    if (processed % commitSize == 0)
                    {
                        await transaction.CommitAsync(ct);
                        await transaction.DisposeAsync();
                        transaction = (SqliteTransaction)await dbConn.BeginTransactionAsync(ct);
                    }
                }
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
            finally { await transaction.DisposeAsync(); }
            return (processed, updated);
        }

        public override string GetDisplayIcon() => "✏️";
    }
}
