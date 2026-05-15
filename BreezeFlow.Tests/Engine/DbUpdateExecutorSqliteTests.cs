using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using BreezeFlow.Engine;
using Xunit;

namespace BreezeFlow.Tests.Engine
{
    public class DbUpdateExecutorSqliteTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DbUpdateExecutorSqliteTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"breezeflow_upd_{Guid.NewGuid():N}.db");
            _connectionString = $"Data Source={_dbPath}";

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE items (
                    id INTEGER PRIMARY KEY,
                    label TEXT,
                    qty INTEGER
                );
                INSERT INTO items VALUES (1, 'one', 10);
                INSERT INTO items VALUES (2, 'two', 20);
                INSERT INTO items VALUES (3, 'three', 30);";
            cmd.ExecuteNonQuery();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(_dbPath); } catch { /* best effort */ }
        }

        private Dictionary<string, object?> Settings(params (string key, object? value)[] entries)
        {
            var s = new Dictionary<string, object?> { ["ConnectionString"] = _connectionString };
            foreach (var (k, v) in entries) s[k] = v;
            return s;
        }

        [Fact]
        public async Task ExistingKey_UpdatesRow()
        {
            var settings = Settings(
                ("TableName", "items"),
                ("KeyFields", "id"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 1L, ["label"] = "one-renamed", ["qty"] = 999L }
            };

            var result = await DbUpdateExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            Assert.Equal(1, result.Processed);
            Assert.Equal(1, result.Updated);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT label, qty FROM items WHERE id = 1";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("one-renamed", reader.GetString(0));
            Assert.Equal(999L, reader.GetInt64(1));
        }

        [Fact]
        public async Task MissingKey_DoesNotInsert()
        {
            var settings = Settings(
                ("TableName", "items"),
                ("KeyFields", "id"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 999L, ["label"] = "nope", ["qty"] = 0L }
            };

            var result = await DbUpdateExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            Assert.Equal(1, result.Processed);
            Assert.Equal(0, result.Updated);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM items";
            Assert.Equal(3L, (long)(cmd.ExecuteScalar() ?? 0L));
        }

        [Fact]
        public async Task CaseInsensitiveKeyLookup()
        {
            var settings = Settings(
                ("TableName", "items"),
                ("KeyFields", "ID"));
            var input = new List<Dictionary<string, object?>>
            {
                // 入力カラム名は小文字、KeyFields は大文字
                new() { ["id"] = 2L, ["label"] = "two-updated", ["qty"] = 22L }
            };

            var result = await DbUpdateExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            Assert.Equal(1, result.Updated);
        }

        [Fact]
        public async Task UpdateFields_LimitsColumnsToUpdate()
        {
            var settings = Settings(
                ("TableName", "items"),
                ("KeyFields", "id"),
                ("UpdateFields", "label"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 3L, ["label"] = "three-renamed", ["qty"] = 999L }
            };

            await DbUpdateExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT label, qty FROM items WHERE id = 3";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("three-renamed", reader.GetString(0));
            // qty は UpdateFields に無いので元値のまま
            Assert.Equal(30L, reader.GetInt64(1));
        }

        [Fact]
        public async Task NoKeyFields_Throws()
        {
            var settings = Settings(("TableName", "items"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 1L, ["label"] = "x" }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DbUpdateExecutor.ExecuteAsync(
                    SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None));
        }

        [Fact]
        public async Task EmptyInput_NoOp()
        {
            var settings = Settings(("TableName", "items"), ("KeyFields", "id"));
            var log = new List<string>();

            var result = await DbUpdateExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings,
                new List<Dictionary<string, object?>>(), new SyncProgress(log), CancellationToken.None);

            Assert.Equal(0, result.Processed);
            Assert.Contains(log, m => m.Contains("入力データなし"));
        }

        [Fact]
        public async Task PartialMatch_WarnsAboutMissingKeys()
        {
            var settings = Settings(("TableName", "items"), ("KeyFields", "id"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 1L, ["label"] = "one-updated", ["qty"] = 11L },
                new() { ["id"] = 999L, ["label"] = "missing", ["qty"] = 0L }
            };
            var log = new List<string>();

            var result = await DbUpdateExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(log), CancellationToken.None);

            Assert.Equal(2, result.Processed);
            Assert.Equal(1, result.Updated);
            Assert.Contains(log, m => m.Contains("一部のキーが DB に存在しません"));
        }
    }
}
