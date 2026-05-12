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
using SampleELT.Models;

namespace SampleELT.Dialogs
{
    public partial class ConnectionManagerDialog : Window
    {
        private DbConnectionInfo? _currentConnection;
        private bool _suppressChangeEvents;

        public ConnectionManagerDialog(Guid? initialSelectionId = null)
        {
            InitializeComponent();
            ConnectionListBox.ItemsSource = ConnectionRegistry.Instance.Connections;

            if (initialSelectionId.HasValue)
            {
                var target = ConnectionRegistry.Instance.Connections
                    .FirstOrDefault(c => c.Id == initialSelectionId.Value);
                if (target != null) ConnectionListBox.SelectedItem = target;
            }
        }

        private void ConnectionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EditPanel == null) return;

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

            // DbType をリアルタイム更新 → DisplayName (🔶/🐬/🐘/🟦/🦭/🪶) も即座に反映
            if (_currentConnection != null)
                _currentConnection.DbType = dbType;
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
            var conn = new DbConnectionInfo
            {
                Name = "新しい接続",
                DbType = DbType.Oracle,
                ConnectionString = ""
            };
            ConnectionRegistry.Instance.Connections.Add(conn);
            ConnectionListBox.SelectedItem = conn;
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

        private void SaveConnection_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConnection == null) return;

            if (string.IsNullOrWhiteSpace(ConnNameBox.Text))
            {
                MessageBox.Show("接続名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentConnection.Name = ConnNameBox.Text.Trim();

            var dbType = ParseSelectedDbType();
            _currentConnection.DbType = dbType;

            switch (dbType)
            {
                case DbType.Oracle:
                    if (string.IsNullOrWhiteSpace(OracleServerBox.Text))
                    {
                        MessageBox.Show("サーバーを入力してください。", "入力エラー",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    _currentConnection.ConnectionString = BuildOracleConnectionString();
                    break;
                case DbType.PostgreSQL:
                    if (string.IsNullOrWhiteSpace(PgServerBox.Text))
                    {
                        MessageBox.Show("サーバーを入力してください。", "入力エラー",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    _currentConnection.ConnectionString = BuildPostgreSQLConnectionString();
                    break;
                case DbType.SqlServer:
                    if (string.IsNullOrWhiteSpace(MssServerBox.Text))
                    {
                        MessageBox.Show("サーバーを入力してください。", "入力エラー",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    _currentConnection.ConnectionString = BuildSqlServerConnectionString();
                    break;
                case DbType.Sqlite:
                    if (string.IsNullOrWhiteSpace(SqlitePathBox.Text))
                    {
                        MessageBox.Show("データベースファイルのパスを指定してください。", "入力エラー",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    _currentConnection.ConnectionString = BuildSqliteConnectionString();
                    break;
                default:
                    // MySQL / MariaDB は同じ MySqlConnector ドライバを使用
                    _currentConnection.ConnectionString = BuildMySQLConnectionString();
                    break;
            }

            ConnectionRegistry.Instance.Save();

            MessageBox.Show("保存しました。", "完了",
                MessageBoxButton.OK, MessageBoxImage.Information);
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

        /// <summary>
        /// "host:port/service" 形式の DataSource を分解する。
        /// </summary>
        private static void ParseOracleDataSource(
            string dataSource, out string host, out string port, out string service)
        {
            host = "localhost"; port = "1521"; service = "ORCL";
            if (string.IsNullOrWhiteSpace(dataSource)) return;

            var colonIdx = dataSource.IndexOf(':');
            var slashIdx = dataSource.IndexOf('/');

            if (colonIdx > 0 && slashIdx > colonIdx)
            {
                host    = dataSource[..colonIdx];
                port    = dataSource[(colonIdx + 1)..slashIdx];
                service = dataSource[(slashIdx + 1)..];
            }
            else if (slashIdx > 0)
            {
                host    = dataSource[..slashIdx];
                service = dataSource[(slashIdx + 1)..];
            }
            else
            {
                host = dataSource; // TNS 名などそのまま使用
            }
        }

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
