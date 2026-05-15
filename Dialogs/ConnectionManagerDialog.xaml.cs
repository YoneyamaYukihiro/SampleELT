using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using BreezeFlow.Models;

namespace BreezeFlow.Dialogs
{
    public partial class ConnectionManagerDialog : Window
    {
        private DbConnectionInfo? _currentConnection;
        private bool _suppressChangeEvents;
        private readonly Pipeline? _referencePipeline;

        public ConnectionManagerDialog(Guid? initialSelectionId = null, Pipeline? referencePipeline = null)
        {
            InitializeComponent();
            _referencePipeline = referencePipeline;
            ConnectionListBox.ItemsSource = ConnectionRegistry.Instance.Connections;

            if (initialSelectionId.HasValue)
            {
                var target = ConnectionRegistry.Instance.Connections
                    .FirstOrDefault(c => c.Id == initialSelectionId.Value);
                if (target != null) ConnectionListBox.SelectedItem = target;
            }
        }

        /// <summary>
        /// 現在のパイプラインで <paramref name="conn"/> を参照しているステップ名一覧を返す。
        /// パイプラインが渡されていなければ空リスト。
        /// </summary>
        private System.Collections.Generic.List<string> FindReferencingSteps(DbConnectionInfo conn)
        {
            var result = new System.Collections.Generic.List<string>();
            if (_referencePipeline == null) return result;

            foreach (var step in _referencePipeline.Steps)
            {
                if (!step.Settings.TryGetValue("ConnectionId", out var idObj) || idObj == null)
                    continue;
                if (Guid.TryParse(idObj.ToString(), out var id) && id == conn.Id)
                    result.Add(step.Name);
            }
            return result;
        }

        private void UpdateReferenceLabel(DbConnectionInfo conn)
        {
            if (ReferenceCountText == null) return;
            var refs = FindReferencingSteps(conn);
            if (refs.Count == 0)
            {
                ReferenceCountText.Text = _referencePipeline == null
                    ? ""
                    : "現在のパイプラインからの参照: 0 ステップ";
                ReferenceCountText.Foreground =
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x61, 0x61, 0x61));
            }
            else
            {
                ReferenceCountText.Text =
                    $"⚠ 現在のパイプラインで {refs.Count} 個のステップから参照中: "
                    + string.Join(", ", refs);
                ReferenceCountText.Foreground =
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD3, 0x2F, 0x2F));
            }
        }

        private void ConnectionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EditPanel == null) return;

            // 1) 古い選択のフォーム入力を自動保存
            if (!_suppressChangeEvents
                && e.RemovedItems.Count > 0
                && e.RemovedItems[0] is DbConnectionInfo prev
                && ConnectionRegistry.Instance.Connections.Contains(prev))
            {
                SaveEditsToModel(prev);
                ConnectionRegistry.Instance.Save();
            }

            // 2) 新しい選択をフォームへロード
            if (ConnectionListBox.SelectedItem is not DbConnectionInfo conn)
            {
                EditPanel.IsEnabled = false;
                _currentConnection = null;
                return;
            }

            _currentConnection = conn;
            EditPanel.IsEnabled = true;
            LoadConnectionToForm(conn);
        }

        private void LoadConnectionToForm(DbConnectionInfo conn)
        {
            _suppressChangeEvents = true;

            ConnNameBox.Text = conn.Name;
            UpdateSectionVisibility(conn.DbType);
            DbTypeCombo.SelectedIndex = DbTypeToComboIndex(conn.DbType);
            EnvironmentCombo.SelectedIndex = EnvironmentToComboIndex(conn.Environment);
            ReadOnlyCheck.IsChecked = conn.IsReadOnly;
            UpdateReferenceLabel(conn);

            switch (conn.DbType)
            {
                case DbType.Oracle:
                    try
                    {
                        var builder = new OracleConnectionStringBuilder(conn.ConnectionString);
                        ParseOracleDataSource(builder.DataSource ?? "",
                            out var host, out var port, out var service);
                        OracleServerBox.Text   = host;
                        OraclePortBox.Text     = port;
                        OracleServiceBox.Text  = service;
                        OracleUserBox.Text     = builder.UserID;
                        OraclePassBox.Password = builder.Password;
                    }
                    catch
                    {
                        OracleServerBox.Text  = "localhost";
                        OraclePortBox.Text    = "1521";
                        OracleServiceBox.Text = "ORCL";
                    }
                    break;
                case DbType.MySQL:
                case DbType.MariaDB:
                    try
                    {
                        var builder = new MySqlConnectionStringBuilder(conn.ConnectionString);
                        MySQLServerBox.Text   = builder.Server;
                        MySQLPortBox.Text     = builder.Port.ToString();
                        MySQLDatabaseBox.Text = builder.Database;
                        MySQLUserBox.Text     = builder.UserID;
                        MySQLPassBox.Password = builder.Password;
                    }
                    catch
                    {
                        MySQLServerBox.Text = "localhost";
                        MySQLPortBox.Text   = "3306";
                    }
                    break;
                case DbType.PostgreSQL:
                    try
                    {
                        var builder = new NpgsqlConnectionStringBuilder(conn.ConnectionString);
                        PgServerBox.Text   = builder.Host ?? "localhost";
                        PgPortBox.Text     = builder.Port.ToString();
                        PgDatabaseBox.Text = builder.Database ?? "";
                        PgUserBox.Text     = builder.Username ?? "postgres";
                        PgPassBox.Password = builder.Password ?? "";
                    }
                    catch
                    {
                        PgServerBox.Text = "localhost";
                        PgPortBox.Text   = "5432";
                    }
                    break;
                case DbType.SqlServer:
                    try
                    {
                        var builder = new SqlConnectionStringBuilder(conn.ConnectionString);
                        ParseSqlServerDataSource(builder.DataSource ?? "",
                            out var host, out var port);
                        MssServerBox.Text   = host;
                        MssPortBox.Text     = port;
                        MssDatabaseBox.Text = builder.InitialCatalog ?? "";
                        MssIntegratedSecurityCheck.IsChecked = builder.IntegratedSecurity;
                        MssUserBox.Text     = builder.UserID ?? "sa";
                        MssPassBox.Password = builder.Password ?? "";
                    }
                    catch
                    {
                        MssServerBox.Text = "localhost";
                        MssPortBox.Text   = "1433";
                    }
                    break;
                case DbType.Sqlite:
                    try
                    {
                        var builder = new SqliteConnectionStringBuilder(conn.ConnectionString);
                        SqlitePathBox.Text = builder.DataSource ?? "";
                    }
                    catch
                    {
                        SqlitePathBox.Text = "";
                    }
                    break;
            }

            _suppressChangeEvents = false;
        }

        private void UpdateSectionVisibility(DbType dbType)
        {
            OracleSection.Visibility     = dbType == DbType.Oracle     ? Visibility.Visible : Visibility.Collapsed;
            PostgreSQLSection.Visibility = dbType == DbType.PostgreSQL ? Visibility.Visible : Visibility.Collapsed;
            MySQLSection.Visibility      = (dbType == DbType.MySQL || dbType == DbType.MariaDB) ? Visibility.Visible : Visibility.Collapsed;
            SqlServerSection.Visibility  = dbType == DbType.SqlServer  ? Visibility.Visible : Visibility.Collapsed;
            SqliteSection.Visibility     = dbType == DbType.Sqlite     ? Visibility.Visible : Visibility.Collapsed;
        }

        private static int DbTypeToComboIndex(DbType dbType) => dbType switch
        {
            DbType.Oracle     => 0,
            DbType.MySQL      => 1,
            DbType.PostgreSQL => 2,
            DbType.SqlServer  => 3,
            DbType.MariaDB    => 4,
            DbType.Sqlite     => 5,
            _                 => 0
        };

        private static int EnvironmentToComboIndex(DbEnvironment env) => env switch
        {
            DbEnvironment.Development => 0,
            DbEnvironment.Staging     => 1,
            DbEnvironment.Production  => 2,
            _                         => 0
        };

        private DbEnvironment ParseSelectedEnvironment()
        {
            var tag = (EnvironmentCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            return tag switch
            {
                "Staging"    => DbEnvironment.Staging,
                "Production" => DbEnvironment.Production,
                _            => DbEnvironment.Development
            };
        }

        private void EnvironmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressChangeEvents || _currentConnection == null) return;

            var prevEnv = _currentConnection.Environment;
            var newEnv = ParseSelectedEnvironment();
            if (prevEnv == newEnv) return;

            // Production への昇格は明示確認 (誤操作で本番扱いになるのを防ぐ)
            if (newEnv == DbEnvironment.Production && prevEnv != DbEnvironment.Production)
            {
                var result = MessageBox.Show(
                    $"接続「{_currentConnection.Name}」を Production としてマークしますか？\n\n" +
                    "この接続を使う書き込み系ステップ (DB Output / Delete / Update / Insert/Update / Exec SQL) は、" +
                    "実行前に確認ダイアログが表示されるようになります。",
                    "Production マークの確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    // ロールバック
                    _suppressChangeEvents = true;
                    EnvironmentCombo.SelectedIndex = EnvironmentToComboIndex(prevEnv);
                    _suppressChangeEvents = false;
                    return;
                }
            }

            _currentConnection.Environment = newEnv;
            ConnectionRegistry.Instance.Save();
        }

        private void ReadOnlyCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressChangeEvents || _currentConnection == null) return;
            _currentConnection.IsReadOnly = ReadOnlyCheck.IsChecked == true;
            ConnectionRegistry.Instance.Save();
        }

        /// <summary>"host,port" or "host\instance" 形式の DataSource を分解する。</summary>
        private static void ParseSqlServerDataSource(
            string dataSource, out string host, out string port)
        {
            host = "localhost"; port = "1433";
            if (string.IsNullOrWhiteSpace(dataSource)) return;

            var commaIdx = dataSource.IndexOf(',');
            if (commaIdx > 0)
            {
                host = dataSource[..commaIdx];
                port = dataSource[(commaIdx + 1)..];
            }
            else
            {
                host = dataSource;
            }
        }

        private void DbTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressChangeEvents) return;
            if (OracleSection == null || MySQLSection == null
                || PostgreSQLSection == null || SqlServerSection == null
                || SqliteSection == null) return;

            var dbType = ParseSelectedDbType();
            UpdateSectionVisibility(dbType);

            // DbType を更新 → DisplayName (🔶/🐬/🐘/🟦/🦭/🪶) も即座に反映 → 自動保存
            if (_currentConnection != null)
            {
                _currentConnection.DbType = dbType;
                _currentConnection.ConnectionString = BuildConnectionStringFor(dbType);
                ConnectionRegistry.Instance.Save();
            }
        }

        /// <summary>
        /// 現在表示中の入力フィールドから ConnectionString を組み立てる。
        /// </summary>
        private string BuildConnectionStringFor(DbType dbType) => dbType switch
        {
            DbType.Oracle     => BuildOracleConnectionString(),
            DbType.PostgreSQL => BuildPostgreSQLConnectionString(),
            DbType.SqlServer  => BuildSqlServerConnectionString(),
            DbType.Sqlite     => BuildSqliteConnectionString(),
            _                 => BuildMySQLConnectionString()  // MySQL / MariaDB
        };

        /// <summary>
        /// 編集中フォームの値を <paramref name="conn"/> に反映する (ファイル保存はしない)。
        /// 接続名が空のときはモデル更新を行わない。
        /// </summary>
        private void SaveEditsToModel(DbConnectionInfo conn)
        {
            if (string.IsNullOrWhiteSpace(ConnNameBox.Text)) return;

            var newName = ConnNameBox.Text.Trim();
            WarnIfDuplicateName(conn, newName);

            conn.Name = newName;
            conn.DbType = ParseSelectedDbType();
            conn.ConnectionString = BuildConnectionStringFor(conn.DbType);
            conn.Environment = ParseSelectedEnvironment();
            conn.IsReadOnly = ReadOnlyCheck.IsChecked == true;
        }

        /// <summary>
        /// 接続名が他のエントリと重複しそうなら 1 度だけ警告する (ブロックはしない)。
        /// </summary>
        private void WarnIfDuplicateName(DbConnectionInfo conn, string newName)
        {
            if (string.Equals(conn.Name, newName, StringComparison.Ordinal)) return;
            if (_dupNameWarned.Contains(newName)) return;

            var clash = ConnectionRegistry.Instance.Connections
                .Any(c => c.Id != conn.Id
                    && string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase));
            if (!clash) return;

            _dupNameWarned.Add(newName);
            MessageBox.Show(
                $"接続名「{newName}」は別の接続と重複しています。\n" +
                "ステップ設定ダイアログで紛らわしくなるため、名前を変えることをお勧めします。\n" +
                "(この警告は同じ名前については一度だけ表示されます)",
                "重複した接続名",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private readonly System.Collections.Generic.HashSet<string> _dupNameWarned =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 現在編集中の接続が「＋ 追加」直後の未編集デフォルト状態か判定する。
        /// 接続名が「新しい接続」のまま、かつ現セクションのサーバー / ファイルパスが
        /// 空または XAML 既定値 (localhost) のままなら true。
        /// </summary>
        private bool IsCurrentEntryUnedited()
        {
            if (_currentConnection == null) return false;
            if (ConnNameBox.Text?.Trim() != "新しい接続") return false;

            var dbType = ParseSelectedDbType();
            string serverLike = dbType switch
            {
                DbType.Oracle     => OracleServerBox.Text?.Trim() ?? "",
                DbType.MySQL or DbType.MariaDB => MySQLServerBox.Text?.Trim() ?? "",
                DbType.PostgreSQL => PgServerBox.Text?.Trim() ?? "",
                DbType.SqlServer  => MssServerBox.Text?.Trim() ?? "",
                DbType.Sqlite     => SqlitePathBox.Text?.Trim() ?? "",
                _                 => ""
            };
            return string.IsNullOrEmpty(serverLike) || serverLike == "localhost";
        }

        private DbType ParseSelectedDbType()
        {
            var tag = (DbTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            return tag switch
            {
                "Oracle"     => DbType.Oracle,
                "MySQL"      => DbType.MySQL,
                "PostgreSQL" => DbType.PostgreSQL,
                "SqlServer"  => DbType.SqlServer,
                "MariaDB"    => DbType.MariaDB,
                "Sqlite"     => DbType.Sqlite,
                _            => DbType.Oracle
            };
        }

        private void EditField_Changed(object sender, EventArgs e)
        {
            if (_suppressChangeEvents || _currentConnection == null) return;

            // 接続名をリアルタイム更新 → ListBox の表示が入力と同時に変わる
            if (sender == ConnNameBox)
                _currentConnection.Name = ConnNameBox.Text;
        }

        private void AddConnection_Click(object sender, RoutedEventArgs e)
        {
            // 現在のエントリが未編集デフォルトのままなら連打をブロック
            if (IsCurrentEntryUnedited())
            {
                MessageBox.Show(
                    "現在編集中の「新しい接続」に接続名とサーバー（または SQLite のファイルパス）を入力してから、次の接続を追加してください。",
                    "情報",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                ConnNameBox.Focus();
                ConnNameBox.SelectAll();
                return;
            }

            var conn = new DbConnectionInfo
            {
                Name = "新しい接続",
                DbType = DbType.Oracle,
                ConnectionString = ""
            };
            ConnectionRegistry.Instance.Connections.Add(conn);
            // この時点で旧選択は SelectionChanged → SaveEditsToModel + Save で永続化される
            ConnectionListBox.SelectedItem = conn;
            ConnectionRegistry.Instance.Save();
            ConnNameBox.Focus();
            ConnNameBox.SelectAll();
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            var idx = ConnectionListBox.SelectedIndex;
            if (idx <= 0) return;
            ConnectionRegistry.Instance.Connections.Move(idx, idx - 1);
            ConnectionRegistry.Instance.Save();
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            var idx = ConnectionListBox.SelectedIndex;
            var coll = ConnectionRegistry.Instance.Connections;
            if (idx < 0 || idx >= coll.Count - 1) return;
            coll.Move(idx, idx + 1);
            ConnectionRegistry.Instance.Save();
        }

        private void DeleteConnection_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConnection == null) return;

            var result = MessageBox.Show(
                $"「{_currentConnection.Name}」を削除しますか？",
                "削除の確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            ConnectionRegistry.Instance.Connections.Remove(_currentConnection);
            ConnectionRegistry.Instance.Save();
            _currentConnection = null;
            EditPanel.IsEnabled = false;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // ダイアログ閉じる時に編集中の内容を自動保存
            if (_currentConnection != null
                && ConnectionRegistry.Instance.Connections.Contains(_currentConnection))
            {
                SaveEditsToModel(_currentConnection);
                ConnectionRegistry.Instance.Save();
            }
            base.OnClosing(e);
        }

        private string BuildMySQLConnectionString()
        {
            if (!uint.TryParse(MySQLPortBox.Text.Trim(), out uint port)) port = 3306;

            var builder = new MySqlConnectionStringBuilder
            {
                Server = MySQLServerBox.Text.Trim(),
                Port = port,
                Database = MySQLDatabaseBox.Text.Trim(),
                UserID = MySQLUserBox.Text.Trim(),
                Password = MySQLPassBox.Password
            };
            return builder.ConnectionString;
        }

        private string BuildSqliteConnectionString()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = SqlitePathBox.Text.Trim()
            };
            return builder.ConnectionString;
        }

        private void SqliteBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "SQLite データベースファイルを選択",
                Filter = "SQLite DB (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|すべてのファイル (*.*)|*.*",
                CheckFileExists = false
            };
            if (!string.IsNullOrEmpty(SqlitePathBox.Text))
            {
                try { dialog.InitialDirectory = System.IO.Path.GetDirectoryName(SqlitePathBox.Text); }
                catch { /* ignore invalid path */ }
            }
            if (dialog.ShowDialog() == true)
            {
                SqlitePathBox.Text = dialog.FileName;
            }
        }

        private string BuildSqlServerConnectionString()
        {
            if (!int.TryParse(MssPortBox.Text.Trim(), out int port)) port = 1433;
            var server = MssServerBox.Text.Trim();
            var dataSource = port == 1433 ? server : $"{server},{port}";

            var builder = new SqlConnectionStringBuilder
            {
                DataSource              = dataSource,
                InitialCatalog          = MssDatabaseBox.Text.Trim(),
                IntegratedSecurity      = MssIntegratedSecurityCheck.IsChecked == true,
                TrustServerCertificate  = true
            };
            if (!builder.IntegratedSecurity)
            {
                builder.UserID   = MssUserBox.Text.Trim();
                builder.Password = MssPassBox.Password;
            }
            return builder.ConnectionString;
        }

        private string BuildPostgreSQLConnectionString()
        {
            if (!int.TryParse(PgPortBox.Text.Trim(), out int port)) port = 5432;

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host     = PgServerBox.Text.Trim(),
                Port     = port,
                Database = PgDatabaseBox.Text.Trim(),
                Username = PgUserBox.Text.Trim(),
                Password = PgPassBox.Password
            };
            return builder.ConnectionString;
        }

        private string BuildOracleConnectionString()
        {
            if (!int.TryParse(OraclePortBox.Text.Trim(), out int port)) port = 1521;
            var dataSource = $"{OracleServerBox.Text.Trim()}:{port}/{OracleServiceBox.Text.Trim()}";
            var builder = new OracleConnectionStringBuilder
            {
                DataSource = dataSource,
                UserID     = OracleUserBox.Text.Trim(),
                Password   = OraclePassBox.Password
            };
            return builder.ConnectionString;
        }

        private static void ParseOracleDataSource(
            string dataSource, out string host, out string port, out string service)
            => BreezeFlow.Tools.OracleDataSourceParser.Parse(dataSource, out host, out port, out service);

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConnection == null)
            {
                MessageBox.Show("接続を選択してください。", "接続テスト",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dbType = ParseSelectedDbType();
            var cs = dbType switch
            {
                DbType.Oracle     => BuildOracleConnectionString(),
                DbType.PostgreSQL => BuildPostgreSQLConnectionString(),
                DbType.SqlServer  => BuildSqlServerConnectionString(),
                DbType.Sqlite     => BuildSqliteConnectionString(),
                _                 => BuildMySQLConnectionString()
            };

            try
            {
                switch (dbType)
                {
                    case DbType.Oracle:
                        using (var conn = new OracleConnection(cs))
                            await conn.OpenAsync();
                        break;
                    case DbType.PostgreSQL:
                        using (var conn = new NpgsqlConnection(cs))
                            await conn.OpenAsync();
                        break;
                    case DbType.SqlServer:
                        using (var conn = new SqlConnection(cs))
                            await conn.OpenAsync();
                        break;
                    case DbType.Sqlite:
                        using (var conn = new SqliteConnection(cs))
                            await conn.OpenAsync();
                        break;
                    default:
                        using (var conn = new MySqlConnection(cs))
                            await conn.OpenAsync();
                        break;
                }
                MessageBox.Show("接続成功！", "接続テスト",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"接続失敗:\n{ex.Message}", "接続テスト",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
