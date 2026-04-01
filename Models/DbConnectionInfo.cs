using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SampleELT.Models
{
    public enum DbType
    {
        Oracle,
        MySQL
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

        public string DisplayName => $"{(DbType == DbType.Oracle ? "🔶" : "🐬")} {Name}";
    }
}
