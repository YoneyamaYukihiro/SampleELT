using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SampleELT.Engine;
using SampleELT.Steps;
using Xunit;

namespace SampleELT.Tests.Engine
{
    /// <summary>
    /// SQLite を使って DbInputExecutor / DbOutputExecutor のエンドツーエンド動作を検証する。
    /// プロバイダ越しに同じアルゴリズムが動くことを確認する。
    /// </summary>
    public class DbExecutorSqliteTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DbExecutorSqliteTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sampleelt_test_{Guid.NewGuid():N}.db");
            _connectionString = $"Data Source={_dbPath}";

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE users (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    status TEXT
                );
                INSERT INTO users (id, name, status) VALUES (1, 'Alice', 'active');
                INSERT INTO users (id, name, status) VALUES (2, 'Bob',   'active');
                INSERT INTO users (id, name, status) VALUES (3, 'Carol', 'inactive');";
            cmd.ExecuteNonQuery();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(_dbPath); } catch { /* best effort */ }
        }

        private Dictionary<string, object?> Settings(params (string key, object? value)[] entries)
        {
            var s = new Dictionary<string, object?>
            {
                ["ConnectionString"] = _connectionString
            };
            foreach (var (k, v) in entries) s[k] = v;
            return s;
        }

        // ==================== DbInputExecutor ====================

        [Fact]
        public async Task Input_NoParams_ReturnsAllRows()
        {
            var settings = Settings(("SQL", "SELECT id, name FROM users ORDER BY id"));
            var result = await DbInputExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings,
                new List<Dictionary<string, object?>>(), new SyncProgress(), CancellationToken.None);

            Assert.Equal(3, result.Count);
            Assert.Equal("Alice", result[0]["name"]);
            Assert.Equal("Carol", result[2]["name"]);
        }

        [Fact]
        public async Task Input_NamedPlaceholder_BindsByFieldName()
        {
            var settings = Settings(("SQL", "SELECT name FROM users WHERE id = :{userId}"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["userId"] = 2L }
            };

            var result = await DbInputExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            Assert.Single(result);
            Assert.Equal("Bob", result[0]["name"]);
        }

        [Fact]
        public async Task Input_PositionalPlaceholder_BindsByOrder()
        {
            var settings = Settings(("SQL", "SELECT name FROM users WHERE status = ? AND id < ?"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["s"] = "active", ["maxId"] = 2L }
            };

            var result = await DbInputExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            Assert.Single(result);
            Assert.Equal("Alice", result[0]["name"]);
        }

        [Fact]
        public async Task Input_ExecuteEachRow_LooksUpEveryRow()
        {
            var settings = Settings(
                ("SQL", "SELECT name FROM users WHERE id = :{userId}"),
                ("ExecuteEachRow", "true"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["userId"] = 1L },
                new() { ["userId"] = 3L }
            };

            var result = await DbInputExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            Assert.Equal(2, result.Count);
            Assert.Equal("Alice", result[0]["name"]);
            Assert.Equal("Carol", result[1]["name"]);
        }

        [Fact]
        public async Task Input_NullDbValueReturnsNull()
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO users (id, name, status) VALUES (4, 'Dave', NULL)";
                cmd.ExecuteNonQuery();
            }

            var settings = Settings(("SQL", "SELECT status FROM users WHERE id = 4"));
            var result = await DbInputExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings,
                new List<Dictionary<string, object?>>(), new SyncProgress(), CancellationToken.None);

            Assert.Single(result);
            Assert.Null(result[0]["status"]);
        }

        // ==================== DbOutputExecutor ====================

        [Fact]
        public async Task Output_Insert_AppendsRows()
        {
            var settings = Settings(
                ("TableName", "users"),
                ("Mode", "INSERT"),
                ("CommitSize", "10"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 100L, ["name"] = "Eve",   ["status"] = "active" },
                new() { ["id"] = 101L, ["name"] = "Frank", ["status"] = "active" }
            };

            await DbOutputExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM users WHERE id IN (100, 101)";
            Assert.Equal(2L, (long)(cmd.ExecuteScalar() ?? 0L));
        }

        [Fact]
        public async Task Output_Upsert_UpdatesExistingRow()
        {
            var settings = Settings(
                ("TableName", "users"),
                ("Mode", "UPSERT"),
                ("CommitSize", "10"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 1L, ["name"] = "Alice-Renamed", ["status"] = "active" }
            };

            await DbOutputExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM users WHERE id = 1";
            Assert.Equal("Alice-Renamed", cmd.ExecuteScalar());
        }

        [Fact]
        public async Task Output_EmptyInput_NoOp()
        {
            var settings = Settings(("TableName", "users"), ("Mode", "INSERT"));
            var log = new List<string>();

            await DbOutputExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings,
                new List<Dictionary<string, object?>>(), new SyncProgress(log), CancellationToken.None);

            Assert.Contains(log, m => m.Contains("入力データなし"));
        }

        // ==================== Step wrappers ====================

        [Fact]
        public async Task SqliteInputStep_DelegatesToExecutor()
        {
            var step = new SqliteInputStep();
            step.Settings["ConnectionString"] = _connectionString;
            step.Settings["SQL"] = "SELECT name FROM users ORDER BY id";

            var result = await step.ExecuteAsync(
                new List<Dictionary<string, object?>>(), new SyncProgress(), CancellationToken.None);

            Assert.Equal(3, result.Count);
        }

        [Fact]
        public async Task SqliteOutputStep_DelegatesToExecutor()
        {
            var step = new SqliteOutputStep();
            step.Settings["ConnectionString"] = _connectionString;
            step.Settings["TableName"] = "users";
            step.Settings["Mode"] = "INSERT";
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 200L, ["name"] = "Grace", ["status"] = "active" }
            };

            await step.ExecuteAsync(input, new SyncProgress(), CancellationToken.None);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM users WHERE id = 200";
            Assert.Equal("Grace", cmd.ExecuteScalar());
        }
    }
}
