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
    /// 入力データの各行をキーとしてDBのレコードを削除するステップ。
    /// Settings["ConnectionId"] : 接続ID
    /// Settings["TableName"]    : 削除対象テーブル名
    /// Settings["KeyFields"]    : カンマ区切りのキーフィールド名 (例: "id" または "dept_id,emp_id")
    /// </summary>
    public class DBDeleteStep : StepBase
    {
        public override StepType StepType => StepType.DBDelete;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = ConnectionRegistry.Instance.ResolveConnectionString(Settings);
            var tableName = Settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "";
            var keyFieldsRaw = Settings.TryGetValue("KeyFields", out var kf) ? kf?.ToString() ?? "" : "";

            var keyFields = keyFieldsRaw.Split(',')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();

            if (inputData.Count == 0)
            {
                progress.Report("DB Delete: 入力データなし");
                return inputData;
            }

            if (keyFields.Count == 0)
            {
                throw new InvalidOperationException("DB Delete: キーフィールドが設定されていません。");
            }

            // Detect DB type from connection
            var conn = ConnectionRegistry.Instance.FindConnection(Settings);
            bool isOracle = conn?.DbType == DbType.Oracle;

            int deleted = 0;

            if (isOracle)
            {
                using var dbConn = new OracleConnection(connectionString);
                await dbConn.OpenAsync(ct);
                using var transaction = dbConn.BeginTransaction();
                try
                {
                    var whereClauses = keyFields.Select((k, i) => $"{k} = :p{i}").ToList();
                    var sql = $"DELETE FROM {tableName} WHERE {string.Join(" AND ", whereClauses)}";

                    foreach (var row in inputData)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var cmd = new OracleCommand(sql, dbConn);
                        cmd.Transaction = transaction;
                        for (int i = 0; i < keyFields.Count; i++)
                            cmd.Parameters.Add(new OracleParameter($":p{i}", row.TryGetValue(keyFields[i], out var v) ? v ?? DBNull.Value : DBNull.Value));
                        await cmd.ExecuteNonQueryAsync(ct);
                        deleted++;
                    }
                    await transaction.CommitAsync(ct);
                }
                catch
                {
                    await transaction.RollbackAsync(ct);
                    throw;
                }
            }
            else
            {
                using var dbConn = new MySqlConnection(connectionString);
                await dbConn.OpenAsync(ct);
                using var transaction = await dbConn.BeginTransactionAsync(ct);
                try
                {
                    var whereClauses = keyFields.Select((k, i) => $"{k} = @p{i}").ToList();
                    var sql = $"DELETE FROM {tableName} WHERE {string.Join(" AND ", whereClauses)}";

                    foreach (var row in inputData)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var cmd = new MySqlCommand(sql, dbConn, transaction);
                        for (int i = 0; i < keyFields.Count; i++)
                            cmd.Parameters.AddWithValue($"@p{i}", row.TryGetValue(keyFields[i], out var v) ? v ?? DBNull.Value : DBNull.Value);
                        await cmd.ExecuteNonQueryAsync(ct);
                        deleted++;
                    }
                    await transaction.CommitAsync(ct);
                }
                catch
                {
                    await transaction.RollbackAsync(ct);
                    throw;
                }
            }

            progress.Report($"DB Delete: {deleted}行 削除完了");
            return inputData;
        }

        public override string GetDisplayIcon() => "🗑";
    }
}
