using System;
using System.Text.Json.Serialization;
using System.Windows.Media;
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

    /// <summary>
    /// 接続先の実行環境タグ。本番接続の書き込み操作で追加確認や視覚的な警告を表示するため。
    /// </summary>
    public enum DbEnvironment
    {
        Development,
        Staging,
        Production
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

        /// <summary>
        /// 環境タグ。Production の接続は書き込み系ステップで追加確認が要求される。
        /// 既定: Development。
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayName))]
        [NotifyPropertyChangedFor(nameof(EnvironmentBadge))]
        [NotifyPropertyChangedFor(nameof(EnvironmentColor))]
        [NotifyPropertyChangedFor(nameof(EnvironmentBrush))]
        private DbEnvironment _environment = DbEnvironment.Development;

        /// <summary>
        /// true のとき、この接続は DB Output / Delete / Update / Insert/Update / Exec SQL の
        /// 書き込み・破壊操作で実行時拒否される (SELECT は許容)。
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayName))]
        private bool _isReadOnly;

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                var icon = GetIcon(DbType);
                var ro = IsReadOnly ? " 🔒" : "";
                var env = Environment != DbEnvironment.Development
                    ? $" [{EnvironmentBadge}]"
                    : "";
                return $"{icon}{env} {Name}{ro}";
            }
        }

        /// <summary>環境タグの短縮ラベル (DEV / STG / PRD)。</summary>
        [JsonIgnore]
        public string EnvironmentBadge => Environment switch
        {
            DbEnvironment.Production  => "PRD",
            DbEnvironment.Staging     => "STG",
            _                         => "DEV"
        };

        /// <summary>環境タグに対応する強調表示用色コード。ステップノードの接続ラベル色と一致。</summary>
        [JsonIgnore]
        public string EnvironmentColor => Environment switch
        {
            DbEnvironment.Production  => "#D32F2F",  // 赤
            DbEnvironment.Staging     => "#F57C00",  // オレンジ
            _                         => "#1565C0"   // 青 (DEV)
        };

        /// <summary>環境タグ表示用の SolidColorBrush。XAML の Fill / Stroke にバインド可能。</summary>
        /// <remarks>
        /// Brush は Dispatcher を持つため System.Text.Json でシリアライズすると例外が出て
        /// ConnectionRegistry.Save() が握りつぶす → 設定が永続化されない事故が起きる。
        /// 必ず [JsonIgnore] で除外する。
        /// </remarks>
        [JsonIgnore]
        public Brush EnvironmentBrush => Environment switch
        {
            DbEnvironment.Production  => new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)),
            DbEnvironment.Staging     => new SolidColorBrush(Color.FromRgb(0xF5, 0x7C, 0x00)),
            _                         => new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0))
        };

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
