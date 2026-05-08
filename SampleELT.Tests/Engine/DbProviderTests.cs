using SampleELT.Engine;
using SampleELT.Models;
using Xunit;

namespace SampleELT.Tests.Engine
{
    public class DbProviderTests
    {
        [Fact]
        public void For_MapsKnownDbTypes()
        {
            Assert.IsType<OracleProvider>(DbProvider.For(DbType.Oracle));
            Assert.IsType<MySqlProvider>(DbProvider.For(DbType.MySQL));
            Assert.IsType<MySqlProvider>(DbProvider.For(DbType.MariaDB));
            Assert.IsType<PostgreSqlProvider>(DbProvider.For(DbType.PostgreSQL));
            Assert.IsType<SqlServerProvider>(DbProvider.For(DbType.SqlServer));
            Assert.IsType<SqliteProvider>(DbProvider.For(DbType.Sqlite));
        }

        [Fact]
        public void Oracle_NamedPlaceholderHasFnPrefixAndColon()
        {
            Assert.Equal(":fn_userId", OracleProvider.Instance.FormatNamedSqlPlaceholder("userId"));
            Assert.Equal("fn_userId", OracleProvider.Instance.FormatNamedParamName("userId"));
        }

        [Fact]
        public void Oracle_PositionalPlaceholderUsesColon()
        {
            Assert.Equal(":p3", OracleProvider.Instance.FormatPositionalSqlPlaceholder(3));
            Assert.Equal("p3", OracleProvider.Instance.FormatPositionalParamName(3));
        }

        [Fact]
        public void Oracle_PreprocessSqlStripsTrailingSemicolon()
        {
            Assert.Equal("SELECT 1 FROM DUAL",
                OracleProvider.Instance.PreprocessSql("SELECT 1 FROM DUAL ;  "));
        }

        [Fact]
        public void MySql_UsesAtSignAndBackticks()
        {
            Assert.Equal("@userId", MySqlProvider.Instance.FormatNamedSqlPlaceholder("userId"));
            Assert.Equal("@userId", MySqlProvider.Instance.FormatNamedParamName("userId"));
            Assert.Equal("`users`", MySqlProvider.Instance.Quote("users"));
        }

        [Fact]
        public void MySql_PreprocessConnectionStringEnablesUserVariables()
        {
            var cs = MySqlProvider.Instance.PreprocessConnectionString("server=h;user=u;password=p");
            Assert.Contains("Allow User Variables=True", cs);
        }

        [Fact]
        public void Postgres_UsesDoubleQuotes()
        {
            Assert.Equal("\"users\"", PostgreSqlProvider.Instance.Quote("users"));
            Assert.Equal("@userId", PostgreSqlProvider.Instance.FormatNamedSqlPlaceholder("userId"));
        }

        [Fact]
        public void SqlServer_UsesBrackets()
        {
            Assert.Equal("[users]", SqlServerProvider.Instance.Quote("users"));
        }

        [Fact]
        public void Sqlite_UsesDoubleQuotes()
        {
            Assert.Equal("\"users\"", SqliteProvider.Instance.Quote("users"));
        }

        [Fact]
        public void BuildInsertSql_DefaultUsesProviderQuoteAndPositional()
        {
            var sql = MySqlProvider.Instance.BuildInsertSql("users", new[] { "id", "name" });
            Assert.Equal("INSERT INTO `users` (`id`, `name`) VALUES (@p0, @p1)", sql);
        }

        [Fact]
        public void BuildInsertSql_OracleHasNoQuoting()
        {
            var sql = OracleProvider.Instance.BuildInsertSql("USERS", new[] { "ID", "NAME" });
            Assert.Equal("INSERT INTO USERS (ID, NAME) VALUES (:p0, :p1)", sql);
        }

        [Fact]
        public void BuildUpsertSql_MySqlUsesOnDuplicateKey()
        {
            var sql = MySqlProvider.Instance.BuildUpsertSql("users", new[] { "id", "name" });
            Assert.Contains("ON DUPLICATE KEY UPDATE", sql);
            Assert.Contains("`name` = @p1", sql);
        }

        [Fact]
        public void BuildUpsertSql_PostgresUsesOnConflict()
        {
            var sql = PostgreSqlProvider.Instance.BuildUpsertSql("users", new[] { "id", "name" });
            Assert.Contains("ON CONFLICT (\"id\") DO UPDATE SET", sql);
            Assert.Contains("\"name\" = EXCLUDED.\"name\"", sql);
        }

        [Fact]
        public void BuildUpsertSql_SqliteUsesOnConflict()
        {
            var sql = SqliteProvider.Instance.BuildUpsertSql("users", new[] { "id", "name" });
            Assert.Contains("ON CONFLICT(\"id\") DO UPDATE SET", sql);
            Assert.Contains("\"name\" = excluded.\"name\"", sql);
        }

        [Fact]
        public void BuildUpsertSql_OracleUsesMerge()
        {
            var sql = OracleProvider.Instance.BuildUpsertSql("USERS", new[] { "ID", "NAME" });
            Assert.Contains("MERGE INTO USERS", sql);
            Assert.Contains("WHEN MATCHED", sql);
            Assert.Contains("WHEN NOT MATCHED", sql);
        }

        [Fact]
        public void BuildUpsertSql_SqlServerUsesMerge()
        {
            var sql = SqlServerProvider.Instance.BuildUpsertSql("users", new[] { "id", "name" });
            Assert.Contains("MERGE INTO [users]", sql);
            Assert.Contains("WHEN NOT MATCHED THEN INSERT", sql);
        }
    }
}
