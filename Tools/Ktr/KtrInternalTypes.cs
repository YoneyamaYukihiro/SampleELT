using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SampleELT.Models;

namespace SampleELT.Tools.Ktr
{
    /// <summary>KTR XML 上の <c>&lt;step&gt;</c> 要素のパース結果。</summary>
    internal class KtrStep
    {
        public string Name = "";
        public string Type = "";
        public XElement Element = new("step");
        public int X;
        public int Y;

        public static KtrStep Parse(XElement el)
        {
            var step = new KtrStep
            {
                Name = el.Element("name")?.Value?.Trim() ?? "",
                Type = el.Element("type")?.Value?.Trim() ?? "",
                Element = el
            };
            var gui = el.Element("GUI");
            int.TryParse(gui?.Element("xloc")?.Value, out step.X);
            int.TryParse(gui?.Element("yloc")?.Value, out step.Y);
            return step;
        }
    }

    /// <summary>変換結果として書き出す JSON ステップの中間表現。</summary>
    internal class JsonStep
    {
        public Guid Id = Guid.NewGuid();
        public string Name = "";
        public string StepType = "";
        public int CanvasX;
        public int CanvasY;
        public int NodeWidth = 150;
        public int NodeHeight = 75;
        public Dictionary<string, object?> Settings = new();
    }

    /// <summary>変換結果として書き出すホップ (接続) の中間表現。</summary>
    internal class JsonHop
    {
        public Guid Id = Guid.NewGuid();
        public Guid SourceStepId;
        public Guid TargetStepId;
    }

    /// <summary>
    /// 各 <see cref="StepConverters.IKtrStepConverter"/> に共有させる状態 (接続マップ・警告・SetVariable で
    /// 解決された SQL プレースホルダ名)。
    /// </summary>
    internal class KtrConvertContext
    {
        public Dictionary<string, Guid> ConnectionMap { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Warnings { get; } = new();
        public IReadOnlyList<string> SetVariableFieldOrder { get; set; } = Array.Empty<string>();
    }

    /// <summary>KTR の <c>&lt;connection&gt;</c> から接続情報を解決し Connection Manager に登録する。</summary>
    internal static class KtrConnectionResolver
    {
        public static void Resolve(XElement root, KtrConvertContext ctx, KtrConvertResult result)
        {
            foreach (var connEl in root.Elements("connection"))
            {
                var name = connEl.Element("name")?.Value?.Trim() ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                if (ctx.ConnectionMap.ContainsKey(name)) continue;

                var existing = ConnectionRegistry.Instance.Connections
                    .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    ctx.ConnectionMap[name] = existing.Id;
                    result.MatchedConnections.Add(existing);
                }
                else
                {
                    var info = BuildFromKtr(connEl, name);
                    ctx.ConnectionMap[name] = info.Id;
                    result.NewConnections.Add(info);
                    ctx.Warnings.Add(
                        $"接続 '{name}' は Connection Manager に未登録です。新規 GUID を発行しました。" +
                        " 取り込み後に Connection Manager で接続情報（パスワード等）を完成させてください。");
                }
            }
        }

        private static DbConnectionInfo BuildFromKtr(XElement connEl, string name)
        {
            var typeStr = (connEl.Element("type")?.Value ?? "").Trim().ToUpperInvariant();
            var server = (connEl.Element("server")?.Value ?? "").Trim();
            var port = (connEl.Element("port")?.Value ?? "").Trim();
            var database = (connEl.Element("database")?.Value ?? "").Trim();
            var user = (connEl.Element("username")?.Value ?? "").Trim();

            DbType dbType = typeStr switch
            {
                "ORACLE"      => DbType.Oracle,
                "MYSQL"       => DbType.MySQL,
                "MARIADB"     => DbType.MariaDB,
                "POSTGRESQL"  => DbType.PostgreSQL,
                "MSSQL"       => DbType.SqlServer,
                "MSSQLSERVER" => DbType.SqlServer,
                "SQLSERVER"   => DbType.SqlServer,
                "SQLITE"      => DbType.Sqlite,
                _             => DbType.MySQL
            };

            // パスワードは Pentaho 暗号化のため復元不能。空のまま。
            string connStr = dbType switch
            {
                DbType.Oracle =>
                    $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={server})(PORT={(string.IsNullOrEmpty(port) ? "1521" : port)}))(CONNECT_DATA=(SERVICE_NAME={database})));User Id={user};Password=;",
                DbType.PostgreSQL =>
                    $"Host={server};Port={(string.IsNullOrEmpty(port) ? "5432" : port)};Database={database};Username={user};Password=;",
                DbType.SqlServer =>
                    $"Server={server},{(string.IsNullOrEmpty(port) ? "1433" : port)};Database={database};User Id={user};Password=;TrustServerCertificate=True;",
                DbType.Sqlite =>
                    $"Data Source={database};",
                _ =>
                    $"Server={server};Port={(string.IsNullOrEmpty(port) ? "3306" : port)};Database={database};Uid={user};Pwd=;"
            };

            return new DbConnectionInfo
            {
                Id = Guid.NewGuid(),
                Name = name,
                DbType = dbType,
                ConnectionString = connStr
            };
        }
    }
}
