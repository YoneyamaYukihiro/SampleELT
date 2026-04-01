using System;
using System.Windows;
using System.Windows.Controls;
using MySqlConnector;
using Oracle.ManagedDataAccess.Client;
using SampleELT.Models;

namespace SampleELT.Dialogs
{
    public partial class ConnectionManagerDialog : Window
    {
        private DbConnectionInfo? _currentConnection;
        private bool _suppressChangeEvents;

        public ConnectionManagerDialog()
        {
            InitializeComponent();
            ConnectionListBox.ItemsSource = ConnectionRegistry.Instance.Connections;
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

            if (conn.DbType == DbType.Oracle)
            {
                DbTypeCombo.SelectedIndex = 0;
                OracleSection.Visibility = Visibility.Visible;
                MySQLSection.Visibility = Visibility.Collapsed;
                OracleConnStrBox.Text = conn.ConnectionString;
            }
            else
            {
                DbTypeCombo.SelectedIndex = 1;
                OracleSection.Visibility = Visibility.Collapsed;
                MySQLSection.Visibility = Visibility.Visible;
                // Parse MySQL connection string into fields
                try
                {
                    var builder = new MySqlConnectionStringBuilder(conn.ConnectionString);
                    MySQLServerBox.Text = builder.Server;
                    MySQLPortBox.Text = builder.Port.ToString();
                    MySQLDatabaseBox.Text = builder.Database;
                    MySQLUserBox.Text = builder.UserID;
                    MySQLPassBox.Password = builder.Password;
                }
                catch
                {
                    MySQLServerBox.Text = "localhost";
                    MySQLPortBox.Text = "3306";
                }
            }

            _suppressChangeEvents = false;
        }

        private void DbTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressChangeEvents) return;
            // InitializeComponent() 中はまだ名前付き要素が null のためガード
            if (OracleSection == null || MySQLSection == null) return;

            var isOracle = (DbTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Oracle";
            OracleSection.Visibility = isOracle ? Visibility.Visible : Visibility.Collapsed;
            MySQLSection.Visibility = isOracle ? Visibility.Collapsed : Visibility.Visible;
        }

        private void EditField_Changed(object sender, EventArgs e)
        {
            // No-op: changes are written only on SaveConnection_Click
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

            var isOracle = (DbTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Oracle";
            _currentConnection.DbType = isOracle ? DbType.Oracle : DbType.MySQL;

            if (isOracle)
            {
                if (string.IsNullOrWhiteSpace(OracleConnStrBox.Text))
                {
                    MessageBox.Show("接続文字列を入力してください。", "入力エラー",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _currentConnection.ConnectionString = OracleConnStrBox.Text.Trim();
            }
            else
            {
                _currentConnection.ConnectionString = BuildMySQLConnectionString();
            }

            ConnectionRegistry.Instance.Save();

            // リストを更新するため一旦再選択
            var saved = _currentConnection;
            ConnectionListBox.SelectedItem = null;
            ConnectionListBox.SelectedItem = saved;

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

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            string cs;
            bool isOracle;

            if (_currentConnection != null)
            {
                isOracle = (DbTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Oracle";
                cs = isOracle ? OracleConnStrBox.Text.Trim() : BuildMySQLConnectionString();
            }
            else
            {
                MessageBox.Show("接続を選択してください。", "接続テスト",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            isOracle = (DbTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Oracle";

            try
            {
                if (isOracle)
                {
                    using var conn = new OracleConnection(cs);
                    await conn.OpenAsync();
                }
                else
                {
                    using var conn = new MySqlConnection(cs);
                    await conn.OpenAsync();
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
