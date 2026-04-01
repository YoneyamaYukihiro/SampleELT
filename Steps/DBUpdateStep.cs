using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
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

            var conn = ConnectionRegistry.Instance.FindConnection(Settings);
            bool isOracle = conn?.DbType == DbType.Oracle;

            int updated = 0;

            if (isOracle)
                updated = await ExecuteOracleAsync(connectionString, tableName, keyFields, updateFieldsRaw, inputData, ct);
            else
                updated = await ExecuteMySQLAsync(connectionString, tableName, keyFields, updateFieldsRaw, inputData, ct);

            progress.Report($"DB Update: {updated}行 更新完了");
            return inputData;
        }

        private static async Task<int> ExecuteOracleAsync(
            string connectionString, string tableName,
            List<string> keyFields, string updateFieldsRaw,
            List<Dictionary<string, object?>> inputData, CancellationToken ct)
        {
            using var dbConn = new OracleConnection(connectionString);
            await dbConn.OpenAsync(ct);
            using var transaction = dbConn.BeginTransaction();
            int count = 0;
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var allCols = row.Keys.ToList();

                    var updateCols = string.IsNullOrWhiteSpace(updateFieldsRaw)
                        ? allCols.Where(c => !keyFields.Contains(c)).ToList()
                        : updateFieldsRaw.Split(',').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList();

                    if (updateCols.Count == 0) continue;

                    // Build parameterized UPDATE
                    var setClauses = updateCols.Select((c, i) => $"{c} = :p{i}").ToList();
                    var whereClauses = keyFields.Select((k, i) => $"{k} = :p{updateCols.Count + i}").ToList();

                    var sql = $"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

                    using var cmd = new OracleCommand(sql, dbConn);
                    cmd.Transaction = transaction;
                    for (int i = 0; i < updateCols.Count; i++)
                        cmd.Parameters.Add(new OracleParameter($":p{i}", row.TryGetValue(updateCols[i], out var v) ? v ?? DBNull.Value : DBNull.Value));
                    for (int i = 0; i < keyFields.Count; i++)
                        cmd.Parameters.Add(new OracleParameter($":p{updateCols.Count + i}", row.TryGetValue(keyFields[i], out var v2) ? v2 ?? DBNull.Value : DBNull.Value));

                    await cmd.ExecuteNonQueryAsync(ct);
                    count++;
                }
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
            return count;
        }

        private static async Task<int> ExecuteMySQLAsync(
            string connectionString, string tableName,
            List<string> keyFields, string updateFieldsRaw,
            List<Dictionary<string, object?>> inputData, CancellationToken ct)
        {
            using var dbConn = new MySqlConnection(connectionString);
            await dbConn.OpenAsync(ct);
            using var transaction = await dbConn.BeginTransactionAsync(ct);
            int count = 0;
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var allCols = row.Keys.ToList();

                    var updateCols = string.IsNullOrWhiteSpace(updateFieldsRaw)
                        ? allCols.Where(c => !keyFields.Contains(c)).ToList()
                        : updateFieldsRaw.Split(',').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList();

                    if (updateCols.Count == 0) continue;

                    var setClauses = updateCols.Select((c, i) => $"{c} = @p{i}").ToList();
                    var whereClauses = keyFields.Select((k, i) => $"{k} = @p{updateCols.Count + i}").ToList();

                    var sql = $"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

                    using var cmd = new MySqlCommand(sql, dbConn, transaction);
                    for (int i = 0; i < updateCols.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{i}", row.TryGetValue(updateCols[i], out var v) ? v ?? DBNull.Value : DBNull.Value);
                    for (int i = 0; i < keyFields.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{updateCols.Count + i}", row.TryGetValue(keyFields[i], out var v2) ? v2 ?? DBNull.Value : DBNull.Value);

                    await cmd.ExecuteNonQueryAsync(ct);
                    count++;
                }
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
            return count;
        }

        public override string GetDisplayIcon() => "✏️";
    }
}
