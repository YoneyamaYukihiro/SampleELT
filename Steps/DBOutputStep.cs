using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Steps
{
    /// <summary>
    /// Oracle / MySQL 両対応の統合 DB 出力ステップ。
    /// 設定された DB 接続の種類に応じて OracleOutputStep または MySQLOutputStep に委譲する。
    /// </summary>
    public class DBOutputStep : StepBase
    {
        public override StepType StepType => StepType.DBOutput;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connIdStr = Settings.TryGetValue("ConnectionId", out var c) ? c?.ToString() : null;
            _ = Guid.TryParse(connIdStr, out var connId);
            var connInfo = ConnectionRegistry.Instance.Connections.FirstOrDefault(x => x.Id == connId);

            StepBase inner = connInfo?.DbType switch
            {
                DbType.Oracle     => new OracleOutputStep(),
                DbType.PostgreSQL => new PostgreSQLOutputStep(),
                DbType.SqlServer  => new SqlServerOutputStep(),
                DbType.Sqlite     => new SqliteOutputStep(),
                // MariaDB は MySQL ドライバ (MySqlConnector) を共用
                _                 => new MySQLOutputStep()
            };

            inner.Settings = this.Settings;
            inner.AllInputStreams = this.AllInputStreams;
            return await inner.ExecuteAsync(inputData, progress, ct);
        }

        public override string GetDisplayIcon() => "🔌";
    }
}
