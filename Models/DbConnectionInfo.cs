using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SampleELT.Models
{
    public enum DbType
    {
        Oracle,
        MySQL,
        PostgreSQL,
        SqlServer,
        MariaDB,
        Sqlite
    }

    public partial class DbConnectionInfo : ObservableObject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayName))]
        private string _name = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayName))]
        private DbType _dbType = DbType.Oracle;

        [ObservableProperty]
        private string _connectionString = "";

        public string DisplayName => $"{GetIcon(DbType)} {Name}";

        private static string GetIcon(DbType type) => type switch
        {
            DbType.Oracle     => "🔶",
            DbType.MySQL      => "🐬",
            DbType.PostgreSQL => "🐘",
            DbType.SqlServer  => "🟦",
            DbType.MariaDB    => "🦭",
            DbType.Sqlite     => "🪶",
            _                 => "❔"
        };
    }
}
