using System;

namespace SampleELT.Models
{
    public enum DbType
    {
        Oracle,
        MySQL
    }

    public class DbConnectionInfo
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public DbType DbType { get; set; } = DbType.Oracle;
        public string ConnectionString { get; set; } = "";

        public string DisplayName => $"{(DbType == DbType.Oracle ? "🔶" : "🐬")} {Name}";
    }
}
