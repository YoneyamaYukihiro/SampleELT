using System;
using System.Text.RegularExpressions;

namespace BreezeFlow.Models
{
    /// <summary>
    /// DB 接続の安全性に関わる横断的なヘルパー。
    /// ・ステップが書き込み系か (誤実行で破壊する恐れがあるか)
    /// ・ExecSQL の文字列が DML / DDL を含むか
    /// など、複数箇所で参照される判定をここに集約する。
    /// </summary>
    public static class ConnectionSafety
    {
        /// <summary>
        /// 書き込み・更新・削除を伴うステップ。Production 接続の利用前確認や
        /// Read-only 接続の拒否対象として用いる。
        /// </summary>
        public static bool IsWriteStep(StepType stepType) => stepType switch
        {
            StepType.DBOutput     => true,
            StepType.DBDelete     => true,
            StepType.DBUpdate     => true,
            StepType.InsertUpdate => true,
            StepType.ExecSQL      => true,   // SELECT のみのことも多いが、保守的に書き込み扱い
            // 後方互換の Oracle/MySQL Output も書き込み系
            StepType.OracleOutput => true,
            StepType.MySQLOutput  => true,
            _ => false
        };

        /// <summary>
        /// SQL 文に DML / DDL (INSERT / UPDATE / DELETE / MERGE / DROP / TRUNCATE / ALTER / CREATE) が
        /// 含まれているか粗く判定する。コメントの中の単語も拾うベストエフォート。
        /// </summary>
        public static bool ContainsWriteSql(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return false;
            return Regex.IsMatch(
                sql,
                @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|TRUNCATE|ALTER|CREATE|REPLACE|GRANT|REVOKE)\b",
                RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// 接続文字列から、ホスト / DB 名を抽出してログ用文字列にする。
        /// パスワードは含めない。失敗時は空文字を返す。
        /// </summary>
        public static string DescribeConnection(DbConnectionInfo? info)
        {
            if (info == null) return "(接続未解決)";

            string detail;
            try
            {
                detail = info.DbType switch
                {
                    DbType.Oracle     => DescribeOracle(info.ConnectionString),
                    DbType.MySQL      => DescribeMySQL(info.ConnectionString),
                    DbType.MariaDB    => DescribeMySQL(info.ConnectionString),
                    DbType.PostgreSQL => DescribePostgres(info.ConnectionString),
                    DbType.SqlServer  => DescribeSqlServer(info.ConnectionString),
                    DbType.Sqlite     => DescribeSqlite(info.ConnectionString),
                    _ => ""
                };
            }
            catch
            {
                detail = "";
            }

            var envBadge = info.Environment != DbEnvironment.Development
                ? $"[{info.EnvironmentBadge}] "
                : "";
            var roBadge = info.IsReadOnly ? " 🔒RO" : "";
            return string.IsNullOrEmpty(detail)
                ? $"{envBadge}{info.Name}{roBadge}"
                : $"{envBadge}{info.Name}{roBadge} ({detail})";
        }

        private static string DescribeOracle(string cs)
        {
            // DATA SOURCE=host:port/service もしくは TNS Descriptor
            var ds = MatchKey(cs, @"DATA\s*SOURCE");
            return string.IsNullOrEmpty(ds) ? "" : $"oracle://{ds}";
        }

        private static string DescribeMySQL(string cs)
        {
            var server = MatchKey(cs, @"Server");
            var db = MatchKey(cs, @"Database");
            return string.IsNullOrEmpty(server) ? "" : $"mysql://{server}/{db}";
        }

        private static string DescribePostgres(string cs)
        {
            var host = MatchKey(cs, @"Host");
            var db = MatchKey(cs, @"Database");
            return string.IsNullOrEmpty(host) ? "" : $"postgres://{host}/{db}";
        }

        private static string DescribeSqlServer(string cs)
        {
            var ds = MatchKey(cs, @"Data\s*Source");
            var db = MatchKey(cs, @"Initial\s*Catalog");
            return string.IsNullOrEmpty(ds) ? "" : $"sqlserver://{ds}/{db}";
        }

        private static string DescribeSqlite(string cs)
        {
            var ds = MatchKey(cs, @"Data\s*Source");
            return string.IsNullOrEmpty(ds) ? "" : $"sqlite://{ds}";
        }

        private static string MatchKey(string cs, string keyPattern)
        {
            // key=value;  または  key="value with ;"
            var m = Regex.Match(
                cs,
                $@"(?:^|;)\s*{keyPattern}\s*=\s*(""[^""]*""|[^;]+)",
                RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            var v = m.Groups[1].Value.Trim().Trim('"');
            return v;
        }
    }

    /// <summary>
    /// <see cref="ConnectionSafety"/> で接続解決失敗を表す例外。実行直前に投げて
    /// Engine 全体を停止させる用途。メッセージで原因と該当ステップを明示する。
    /// </summary>
    public class ConnectionResolutionException : InvalidOperationException
    {
        public ConnectionResolutionException(string message) : base(message) { }
    }
}
