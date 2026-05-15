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
    public class DbInsertUpdateExecutorSqliteTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DbInsertUpdateExecutorSqliteTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"breezeflow_iu_{Guid.NewGuid():N}.db");
            _connectionString = $"Data Source={_dbPath}";

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE products (
                    sku TEXT NOT NULL,
                    name TEXT NOT NULL,
                    price INTEGER,
                    PRIMARY KEY (sku)
                );
                INSERT INTO products (sku, name, price) VALUES ('A1', 'Apple', 100);
                INSERT INTO products (sku, name, price) VALUES ('B2', 'Banana', 200);";
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
                ("TableName", "products"),
                ("KeyFields", "sku"),
                ("UpdateFields", "name,price"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["sku"] = "A1", ["name"] = "Apple-Premium", ["price"] = 150L }
            };

            await DbInsertUpdateExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, price FROM products WHERE sku = 'A1'";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("Apple-Premium", reader.GetString(0));
            Assert.Equal(150L, reader.GetInt64(1));
        }

        [Fact]
        public async Task MissingKey_InsertsRow()
        {
            var settings = Settings(
                ("TableName", "products"),
                ("KeyFields", "sku"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["sku"] = "C3", ["name"] = "Cherry", ["price"] = 300L }
            };

            await DbInsertUpdateExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM products WHERE sku = 'C3'";
            Assert.Equal("Cherry", cmd.ExecuteScalar());
        }

        [Fact]
        public async Task CompositeKey_UpdatesAndInserts()
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE composite_test (
                        tenant TEXT, sku TEXT, qty INTEGER,
                        PRIMARY KEY (tenant, sku)
                    );
                    INSERT INTO composite_test VALUES ('t1', 'X', 10);";
                cmd.ExecuteNonQuery();
            }

            var settings = Settings(
                ("TableName", "composite_test"),
                ("KeyFields", "tenant,sku"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["tenant"] = "t1", ["sku"] = "X", ["qty"] = 99L }, // update
                new() { ["tenant"] = "t1", ["sku"] = "Y", ["qty"] = 5L }   // insert
            };

            await DbInsertUpdateExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            using var c = new SqliteConnection(_connectionString);
            c.Open();
            using var cmd2 = c.CreateCommand();
            cmd2.CommandText = "SELECT qty FROM composite_test WHERE tenant='t1' AND sku='X'";
            Assert.Equal(99L, cmd2.ExecuteScalar());
            cmd2.CommandText = "SELECT qty FROM composite_test WHERE tenant='t1' AND sku='Y'";
            Assert.Equal(5L, cmd2.ExecuteScalar());
        }

        [Fact]
        public async Task EmptyInput_NoOp()
        {
            var settings = Settings(("TableName", "products"), ("KeyFields", "sku"));
            var log = new List<string>();

            var result = await DbInsertUpdateExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings,
                new List<Dictionary<string, object?>>(), new SyncProgress(log), CancellationToken.None);

            Assert.Equal(0, result.Processed);
            Assert.Contains(log, m => m.Contains("入力データなし"));
        }

        [Fact]
        public async Task NoKeyFields_Throws()
        {
            var settings = Settings(("TableName", "products"));
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["sku"] = "Z" }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DbInsertUpdateExecutor.ExecuteAsync(
                    SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None));
        }

        [Fact]
        public async Task UpdateFields_FiltersInsertColumns()
        {
            // UpdateFields に "name" のみ指定、price は無視
            var settings = Settings(
                ("TableName", "products"),
                ("KeyFields", "sku"),
                ("UpdateFields", "name"));

            // 既存行の更新で動作確認 (price は対象外なので元値のまま)
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["sku"] = "B2", ["name"] = "Banana-Updated", ["price"] = 999L }
            };

            await DbInsertUpdateExecutor.ExecuteAsync(
                SqliteProvider.Instance, settings, input, new SyncProgress(), CancellationToken.None);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, price FROM products WHERE sku = 'B2'";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("Banana-Updated", reader.GetString(0));
            // UpdateFields に price が無いため、200 のまま (UPDATE 対象外)
            Assert.Equal(200L, reader.GetInt64(1));
        }
    }
}
