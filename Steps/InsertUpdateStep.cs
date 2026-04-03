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
    /// 入力データをキーで検索し、一致すれば UPDATE、なければ INSERT するステップ (UPSERT)。
    /// Settings["ConnectionId"]  : 接続ID
    /// Settings["TableName"]     : 対象テーブル名
    /// Settings["KeyFields"]     : カンマ区切りのキーフィールド名 (例: "id")
    /// Settings["UpdateFields"]  : カンマ区切りの更新フィールド名 (空の場合はキー以外の全フィールド)
    /// </summary>
    public class InsertUpdateStep : StepBase
    {
        public override StepType StepType => StepType.InsertUpdate;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = ConnectionRegistry.Instance.ResolveConnectionString(Settings);
            var tableName = Settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "";
            var keyFieldsRaw = Settings.TryGetValue("KeyFields", out var kf) ? kf?.ToString() ?? "" : "";
            var updateFieldsRaw = Settings.TryGetValue("UpdateFields", out var uf) ? uf?.ToString() ?? "" : "";

            var keyFields = keyFieldsRaw.Split(',')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();

            if (inputData.Count == 0)
            {
                progress.Report("Insert/Update: 入力データなし");
                return inputData;
            }

            if (keyFields.Count == 0)
                throw new InvalidOperationException("Insert/Update: キーフィールドが設定されていません。");

            int commitSize = Settings.TryGetValue("CommitSize", out var cs)
                && int.TryParse(cs?.ToString(), out var csVal) && csVal > 0 ? csVal : 100;

            var conn = ConnectionRegistry.Instance.FindConnection(Settings);
            bool isOracle = conn?.DbType == DbType.Oracle;

            int upserted = 0;

            if (isOracle)
                upserted = await ExecuteOracleAsync(connectionString, tableName, keyFields, updateFieldsRaw, inputData, commitSize, ct);
            else
                upserted = await ExecuteMySQLAsync(connectionString, tableName, keyFields, updateFieldsRaw, inputData, commitSize, ct);

            progress.Report($"Insert/Update: {upserted}行 処理完了");
            return inputData;
        }

        private static async Task<int> ExecuteOracleAsync(
            string connectionString, string tableName,
            List<string> keyFields, string updateFieldsRaw,
            List<Dictionary<string, object?>> inputData, int commitSize, CancellationToken ct)
        {
            using var dbConn = new OracleConnection(connectionString);
            await dbConn.OpenAsync(ct);
            int count = 0;
            var transaction = dbConn.BeginTransaction();
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var allCols = row.Keys.ToList();

                    var updateCols = string.IsNullOrWhiteSpace(updateFieldsRaw)
                        ? allCols.Where(c => !keyFields.Contains(c)).ToList()
                        : updateFieldsRaw.Split(',').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList();

                    var onClause = string.Join(" AND ", keyFields.Select((k, i) => $"t.{k} = s.{k}"));
                    var updateClause = string.Join(", ", updateCols.Select(c => $"t.{c} = s.{c}"));
                    var insertCols = string.Join(", ", allCols);
                    var srcCols = string.Join(", ", allCols.Select((c, i) => $":p{i} AS {c}"));
                    var insertVals = string.Join(", ", allCols.Select(c => $"s.{c}"));

                    var sql = $"MERGE INTO {tableName} t USING (SELECT {srcCols} FROM DUAL) s ON ({onClause}) " +
                              (updateCols.Count > 0 ? $"WHEN MATCHED THEN UPDATE SET {updateClause} " : "") +
                              $"WHEN NOT MATCHED THEN INSERT ({insertCols}) VALUES ({insertVals})";

                    using var cmd = new OracleCommand(sql, dbConn);
                    cmd.Transaction = transaction;
                    for (int i = 0; i < allCols.Count; i++)
                        cmd.Parameters.Add(new OracleParameter($":p{i}", row[allCols[i]] ?? DBNull.Value));

                    await cmd.ExecuteNonQueryAsync(ct);
                    count++;

                    if (count % commitSize == 0)
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
            return count;
        }

        private static async Task<int> ExecuteMySQLAsync(
            string connectionString, string tableName,
            List<string> keyFields, string updateFieldsRaw,
            List<Dictionary<string, object?>> inputData, int commitSize, CancellationToken ct)
        {
            using var dbConn = new MySqlConnection(connectionString);
            await dbConn.OpenAsync(ct);
            int count = 0;
            var transaction = await dbConn.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var allCols = row.Keys.ToList();

                    var updateCols = string.IsNullOrWhiteSpace(updateFieldsRaw)
                        ? allCols.Where(c => !keyFields.Contains(c)).ToList()
                        : updateFieldsRaw.Split(',').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList();

                    var colList = string.Join(", ", allCols);
                    var paramList = string.Join(", ", allCols.Select((c, i) => $"@p{i}"));
                    var updateClause = string.Join(", ", updateCols.Select(c => $"{c}=VALUES({c})"));

                    var sql = $"INSERT INTO {tableName} ({colList}) VALUES ({paramList})" +
                              (updateCols.Count > 0 ? $" ON DUPLICATE KEY UPDATE {updateClause}" : "");

                    using var cmd = new MySqlCommand(sql, dbConn, transaction);
                    for (int i = 0; i < allCols.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{i}", row[allCols[i]] ?? DBNull.Value);

                    await cmd.ExecuteNonQueryAsync(ct);
                    count++;

                    if (count % commitSize == 0)
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
            return count;
        }

        public override string GetDisplayIcon() => "🔄";
    }
}
