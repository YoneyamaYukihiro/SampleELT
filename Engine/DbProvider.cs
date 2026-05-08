using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using SampleELT.Models;

namespace SampleELT.Engine
{
    /// <summary>
    /// DB ごとの差分（接続ファクトリ、プレースホルダー記法、INSERT/UPSERT 構文、識別子クォート）を隠蔽する抽象。
    /// DbInputExecutor / DbOutputExecutor はこのプロバイダ越しにのみ ADO.NET にアクセスする。
    /// </summary>
    public abstract class DbProvider
    {
        public abstract DbConnection CreateConnection(string connectionString);
        public abstract string LogPrefix { get; }

        public virtual string PreprocessConnectionString(string cs) => cs;
        public virtual string PreprocessSql(string sql) => sql;

        /// <summary>名前付き <c>:{field}</c> プレースホルダーを SQL 上で置き換える書式。</summary>
        public virtual string FormatNamedSqlPlaceholder(string fieldName) => $"@{fieldName}";

        /// <summary>名前付きパラメータを <see cref="DbCommand.Parameters"/> に登録する際の名前。</summary>
        public virtual string FormatNamedParamName(string fieldName) => $"@{fieldName}";

        /// <summary>位置型 <c>?</c> プレースホルダーを SQL 上で置き換える書式。</summary>
        public virtual string FormatPositionalSqlPlaceholder(int idx) => $"@p{idx}";

        public virtual string FormatPositionalParamName(int idx) => $"@p{idx}";

        /// <summary>テーブル名・列名のクォート。</summary>
        public virtual string Quote(string ident) => ident;

        public virtual string BuildInsertSql(string tableName, IReadOnlyList<string> columns)
        {
            var cols = string.Join(", ", columns.Select(Quote));
            var ps = string.Join(", ", columns.Select((_, i) => FormatPositionalSqlPlaceholder(i)));
            return $"INSERT INTO {Quote(tableName)} ({cols}) VALUES ({ps})";
        }

        public abstract string BuildUpsertSql(string tableName, IReadOnlyList<string> columns);

        /// <summary>
        /// 明示キーで一致行を UPDATE / 不一致行を INSERT する SQL を組み立てる (Insert/Update ステップ用)。
        /// updateCols が空の場合は INSERT のみ、または ON CONFLICT DO NOTHING に相当する SQL を返す。
        /// </summary>
        public abstract string BuildKeyedUpsertSql(
            string tableName,
            IReadOnlyList<string> allCols,
            IReadOnlyList<string> keyFields,
            IReadOnlyList<string> updateCols);

        /// <summary>
        /// キーで一致行のみ UPDATE する SQL (DB Update ステップ用)。
        /// パラメータ順は updateCols → keyFields。
        /// </summary>
        public virtual string BuildKeyedUpdateSql(
            string tableName,
            IReadOnlyList<string> updateCols,
            IReadOnlyList<string> keyFields)
        {
            var sets = string.Join(", ",
                updateCols.Select((c, i) => $"{Quote(c)} = {FormatPositionalSqlPlaceholder(i)}"));
            var wheres = string.Join(" AND ",
                keyFields.Select((k, i) => $"{Quote(k)} = {FormatPositionalSqlPlaceholder(updateCols.Count + i)}"));
            return $"UPDATE {Quote(tableName)} SET {sets} WHERE {wheres}";
        }

        /// <summary>
        /// MySQL のように <see cref="DbCommand.ExecuteNonQuery"/> 戻り値で
        /// Insert/Update/変更なしの内訳が取れるかどうか。
        /// </summary>
        public virtual bool ProvidesInsertUpdateBreakdown => false;

        public static DbProvider For(DbType dbType) => dbType switch
        {
            DbType.Oracle     => OracleProvider.Instance,
            DbType.PostgreSQL => PostgreSqlProvider.Instance,
            DbType.SqlServer  => SqlServerProvider.Instance,
            DbType.Sqlite     => SqliteProvider.Instance,
            // MariaDB は MySqlConnector で動かす
            _                 => MySqlProvider.Instance
        };
    }

    public sealed class OracleProvider : DbProvider
    {
        public static OracleProvider Instance { get; } = new();
        private OracleProvider() { }

        public override DbConnection CreateConnection(string cs) => new OracleConnection(cs);
        public override string LogPrefix => "Oracle";

        // Oracle は末尾セミコロンを許容しないため除去する
        public override string PreprocessSql(string sql) => sql.TrimEnd().TrimEnd(';').TrimEnd();

        // OracleParameter のコンストラクタは「:」を含めない名前を取り、SQL 側に :プレフィクスを付ける
        public override string FormatNamedSqlPlaceholder(string fieldName) => $":fn_{fieldName}";
        public override string FormatNamedParamName(string fieldName) => $"fn_{fieldName}";
        public override string FormatPositionalSqlPlaceholder(int idx) => $":p{idx}";
        public override string FormatPositionalParamName(int idx) => $"p{idx}";

        public override string BuildUpsertSql(string tableName, IReadOnlyList<string> columns)
        {
            var colNames  = string.Join(", ", columns);
            var paramRefs = string.Join(", ", columns.Select((_, i) => FormatPositionalSqlPlaceholder(i)));
            var srcSelect = string.Join(", ", columns.Select((c, i) => $"{FormatPositionalSqlPlaceholder(i)} AS {c}"));
            var updateSet = string.Join(", ", columns.Select((c, i) => $"{c} = {FormatPositionalSqlPlaceholder(i)}"));
            var keyCol    = columns[0];
            return $"MERGE INTO {tableName} t USING (SELECT {srcSelect} FROM DUAL) s "
                 + $"ON (t.{keyCol} = s.{keyCol}) "
                 + $"WHEN MATCHED THEN UPDATE SET {updateSet} "
                 + $"WHEN NOT MATCHED THEN INSERT ({colNames}) VALUES ({paramRefs})";
        }

        public override string BuildKeyedUpsertSql(
            string tableName,
            IReadOnlyList<string> allCols,
            IReadOnlyList<string> keyFields,
            IReadOnlyList<string> updateCols)
        {
            var on        = string.Join(" AND ", keyFields.Select(k => $"t.{k} = s.{k}"));
            var srcSelect = string.Join(", ", allCols.Select((c, i) => $"{FormatPositionalSqlPlaceholder(i)} AS {c}"));
            var insertCols = string.Join(", ", allCols);
            var insertVals = string.Join(", ", allCols.Select(c => $"s.{c}"));
            var updateSet  = string.Join(", ", updateCols.Select(c => $"t.{c} = s.{c}"));
            return $"MERGE INTO {tableName} t USING (SELECT {srcSelect} FROM DUAL) s ON ({on}) "
                 + (updateCols.Count > 0 ? $"WHEN MATCHED THEN UPDATE SET {updateSet} " : "")
                 + $"WHEN NOT MATCHED THEN INSERT ({insertCols}) VALUES ({insertVals})";
        }
    }

    public sealed class MySqlProvider : DbProvider
    {
        public static MySqlProvider Instance { get; } = new();
        private MySqlProvider() { }

        public override DbConnection CreateConnection(string cs) => new MySqlConnection(cs);
        public override string LogPrefix => "MySQL";

        // SQL に @変数 が含まれる場合に備えて AllowUserVariables を有効化
        public override string PreprocessConnectionString(string cs)
            => new MySqlConnectionStringBuilder(cs) { AllowUserVariables = true }.ConnectionString;

        public override string Quote(string ident) => $"`{ident}`";

        public override bool ProvidesInsertUpdateBreakdown => true;

        public override string BuildUpsertSql(string tableName, IReadOnlyList<string> columns)
        {
            var cols   = string.Join(", ", columns.Select(Quote));
            var ps     = string.Join(", ", columns.Select((_, i) => FormatPositionalSqlPlaceholder(i)));
            var update = string.Join(", ", columns.Select((c, i) => $"{Quote(c)} = {FormatPositionalSqlPlaceholder(i)}"));
            return $"INSERT INTO {Quote(tableName)} ({cols}) VALUES ({ps}) ON DUPLICATE KEY UPDATE {update}";
        }

        public override string BuildKeyedUpsertSql(
            string tableName,
            IReadOnlyList<string> allCols,
            IReadOnlyList<string> keyFields,
            IReadOnlyList<string> updateCols)
        {
            var cols   = string.Join(", ", allCols.Select(Quote));
            var ps     = string.Join(", ", allCols.Select((_, i) => FormatPositionalSqlPlaceholder(i)));
            var update = string.Join(", ", updateCols.Select(c => $"{Quote(c)}=VALUES({Quote(c)})"));
            return $"INSERT INTO {Quote(tableName)} ({cols}) VALUES ({ps})"
                 + (updateCols.Count > 0 ? $" ON DUPLICATE KEY UPDATE {update}" : "");
        }
    }

    public sealed class PostgreSqlProvider : DbProvider
    {
        public static PostgreSqlProvider Instance { get; } = new();
        private PostgreSqlProvider() { }

        public override DbConnection CreateConnection(string cs) => new NpgsqlConnection(cs);
        public override string LogPrefix => "PostgreSQL";

        public override string Quote(string ident) => $"\"{ident}\"";

        public override string BuildUpsertSql(string tableName, IReadOnlyList<string> columns)
        {
            var cols   = string.Join(", ", columns.Select(Quote));
            var ps     = string.Join(", ", columns.Select((_, i) => FormatPositionalSqlPlaceholder(i)));
            var keyCol = columns[0];
            var update = string.Join(", ",
                columns.Where(c => c != keyCol).Select(c => $"{Quote(c)} = EXCLUDED.{Quote(c)}"));
            return $"INSERT INTO {Quote(tableName)} ({cols}) VALUES ({ps}) "
                 + $"ON CONFLICT ({Quote(keyCol)}) DO UPDATE SET {update}";
        }

        public override string BuildKeyedUpsertSql(
            string tableName,
            IReadOnlyList<string> allCols,
            IReadOnlyList<string> keyFields,
            IReadOnlyList<string> updateCols)
        {
            var cols    = string.Join(", ", allCols.Select(Quote));
            var ps      = string.Join(", ", allCols.Select((_, i) => FormatPositionalSqlPlaceholder(i)));
            var conflict = string.Join(", ", keyFields.Select(Quote));
            var update  = string.Join(", ", updateCols.Select(c => $"{Quote(c)} = EXCLUDED.{Quote(c)}"));
            var conflictAction = updateCols.Count > 0
                ? $"DO UPDATE SET {update}"
                : "DO NOTHING";
            return $"INSERT INTO {Quote(tableName)} ({cols}) VALUES ({ps}) "
                 + $"ON CONFLICT ({conflict}) {conflictAction}";
        }
    }

    public sealed class SqlServerProvider : DbProvider
    {
        public static SqlServerProvider Instance { get; } = new();
        private SqlServerProvider() { }

        public override DbConnection CreateConnection(string cs) => new SqlConnection(cs);
        public override string LogPrefix => "SQL Server";

        public override string Quote(string ident) => $"[{ident}]";

        public override string BuildUpsertSql(string tableName, IReadOnlyList<string> columns)
        {
            var cols    = string.Join(", ", columns.Select(Quote));
            var ps      = string.Join(", ", columns.Select((_, i) => FormatPositionalSqlPlaceholder(i)));
            var keyCol  = columns[0];
            var srcSel  = string.Join(", ", columns.Select((c, i) => $"{FormatPositionalSqlPlaceholder(i)} AS {Quote(c)}"));
            var insVals = string.Join(", ", columns.Select(c => $"s.{Quote(c)}"));
            var update  = string.Join(", ",
                columns.Where(c => c != keyCol).Select(c => $"t.{Quote(c)} = s.{Quote(c)}"));
            var setClause = update.Length > 0 ? $"WHEN MATCHED THEN UPDATE SET {update} " : "";
            return $"MERGE INTO {Quote(tableName)} AS t USING (SELECT {srcSel}) AS s "
                 + $"ON t.{Quote(keyCol)} = s.{Quote(keyCol)} "
                 + setClause
                 + $"WHEN NOT MATCHED THEN INSERT ({cols}) VALUES ({insVals});";
        }

        public override string BuildKeyedUpsertSql(
            string tableName,
            IReadOnlyList<string> allCols,
            IReadOnlyList<string> keyFields,
            IReadOnlyList<string> updateCols)
        {
            var on      = string.Join(" AND ", keyFields.Select(k => $"t.{Quote(k)} = s.{Quote(k)}"));
            var srcSel  = string.Join(", ", allCols.Select((c, i) => $"{FormatPositionalSqlPlaceholder(i)} AS {Quote(c)}"));
            var insCols = string.Join(", ", allCols.Select(Quote));
            var insVals = string.Join(", ", allCols.Select(c => $"s.{Quote(c)}"));
            var update  = string.Join(", ", updateCols.Select(c => $"t.{Quote(c)} = s.{Quote(c)}"));
            return $"MERGE INTO {Quote(tableName)} AS t USING (SELECT {srcSel}) AS s ON ({on}) "
                 + (updateCols.Count > 0 ? $"WHEN MATCHED THEN UPDATE SET {update} " : "")
                 + $"WHEN NOT MATCHED THEN INSERT ({insCols}) VALUES ({insVals});";
        }
    }

    public sealed class SqliteProvider : DbProvider
    {
        public static SqliteProvider Instance { get; } = new();
        private SqliteProvider() { }

        public override DbConnection CreateConnection(string cs) => new SqliteConnection(cs);
        public override string LogPrefix => "SQLite";

        public override string Quote(string ident) => $"\"{ident}\"";

        public override string BuildUpsertSql(string tableName, IReadOnlyList<string> columns)
        {
            var cols   = string.Join(", ", columns.Select(Quote));
            var ps     = string.Join(", ", columns.Select((_, i) => FormatPositionalSqlPlaceholder(i)));
            var keyCol = columns[0];
            var update = string.Join(", ",
                columns.Where(c => c != keyCol).Select(c => $"{Quote(c)} = excluded.{Quote(c)}"));
            return $"INSERT INTO {Quote(tableName)} ({cols}) VALUES ({ps}) "
                 + $"ON CONFLICT({Quote(keyCol)}) DO UPDATE SET {update}";
        }

        public override string BuildKeyedUpsertSql(
            string tableName,
            IReadOnlyList<string> allCols,
            IReadOnlyList<string> keyFields,
            IReadOnlyList<string> updateCols)
        {
            var cols     = string.Join(", ", allCols.Select(Quote));
            var ps       = string.Join(", ", allCols.Select((_, i) => FormatPositionalSqlPlaceholder(i)));
            var conflict = string.Join(", ", keyFields.Select(Quote));
            var update   = string.Join(", ", updateCols.Select(c => $"{Quote(c)} = excluded.{Quote(c)}"));
            var conflictAction = updateCols.Count > 0
                ? $"DO UPDATE SET {update}"
                : "DO NOTHING";
            return $"INSERT INTO {Quote(tableName)} ({cols}) VALUES ({ps}) "
                 + $"ON CONFLICT({conflict}) {conflictAction}";
        }
    }
}
