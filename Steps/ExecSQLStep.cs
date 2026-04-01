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
    /// 任意のSQLを実行するステップ。
    /// Settings["ConnectionId"]   : 接続ID
    /// Settings["SQL"]            : 実行するSQL
    /// Settings["ExecuteEachRow"] : "true" の場合、入力データの各行で ? パラメータを設定して実行
    /// </summary>
    public class ExecSQLStep : StepBase
    {
        public override StepType StepType => StepType.ExecSQL;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connectionString = ConnectionRegistry.Instance.ResolveConnectionString(Settings);
            var sql = Settings.TryGetValue("SQL", out var s) ? s?.ToString() ?? "" : "";
            var executeEachRow = Settings.TryGetValue("ExecuteEachRow", out var eer)
                && eer?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            var conn = ConnectionRegistry.Instance.FindConnection(Settings);
            bool isOracle = conn?.DbType == DbType.Oracle;

            if (isOracle)
                await ExecuteOracleAsync(connectionString, sql, executeEachRow, inputData, progress, ct);
            else
                await ExecuteMySQLAsync(connectionString, sql, executeEachRow, inputData, progress, ct);

            return inputData;
        }

        private static async Task ExecuteOracleAsync(
            string connectionString, string sql, bool executeEachRow,
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress, CancellationToken ct)
        {
            using var dbConn = new OracleConnection(connectionString);
            await dbConn.OpenAsync(ct);
            using var transaction = dbConn.BeginTransaction();
            try
            {
                if (executeEachRow && inputData.Count > 0)
                {
                    int executed = 0;
                    foreach (var row in inputData)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var cmd = new OracleCommand(sql, dbConn);
                        cmd.Transaction = transaction;
                        var values = row.Values.ToList();
                        for (int i = 0; i < values.Count; i++)
                            cmd.Parameters.Add(new OracleParameter($":p{i}", values[i] ?? DBNull.Value));
                        await cmd.ExecuteNonQueryAsync(ct);
                        executed++;
                    }
                    await transaction.CommitAsync(ct);
                    progress.Report($"Exec SQL (Oracle): {executed}行 実行完了");
                }
                else
                {
                    using var cmd = new OracleCommand(sql, dbConn);
                    cmd.Transaction = transaction;
                    int rows = await cmd.ExecuteNonQueryAsync(ct);
                    await transaction.CommitAsync(ct);
                    progress.Report($"Exec SQL (Oracle): {rows}行 影響");
                }
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        private static async Task ExecuteMySQLAsync(
            string connectionString, string sql, bool executeEachRow,
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress, CancellationToken ct)
        {
            using var dbConn = new MySqlConnection(connectionString);
            await dbConn.OpenAsync(ct);
            using var transaction = await dbConn.BeginTransactionAsync(ct);
            try
            {
                if (executeEachRow && inputData.Count > 0)
                {
                    int executed = 0;
                    foreach (var row in inputData)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var cmd = new MySqlCommand(sql, dbConn, transaction);
                        var values = row.Values.ToList();
                        for (int i = 0; i < values.Count; i++)
                            cmd.Parameters.AddWithValue($"@p{i}", values[i] ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync(ct);
                        executed++;
                    }
                    await transaction.CommitAsync(ct);
                    progress.Report($"Exec SQL (MySQL): {executed}行 実行完了");
                }
                else
                {
                    using var cmd = new MySqlCommand(sql, dbConn, transaction);
                    int rows = await cmd.ExecuteNonQueryAsync(ct);
                    await transaction.CommitAsync(ct);
                    progress.Report($"Exec SQL (MySQL): {rows}行 影響");
                }
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        public override string GetDisplayIcon() => "⚡";
    }
}
