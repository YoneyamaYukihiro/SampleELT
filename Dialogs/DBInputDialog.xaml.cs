using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MySqlConnector;
using Oracle.ManagedDataAccess.Client;
using SampleELT.Models;

namespace SampleELT.Dialogs
{
    public partial class DBInputDialog : Window
    {
        public string StepName { get; private set; } = "";
        public Guid? ConnectionId { get; private set; }
        public string SQL { get; private set; } = "";
        public bool ExecuteEachRow { get; private set; }

        public DBInputDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, Guid? connectionId, string sql, bool executeEachRow = false)
        {
            StepNameBox.Text = stepName;
            SQLBox.Text = sql;
            ExecuteEachRowCheck.IsChecked = executeEachRow;
            RefreshConnectionList(connectionId);
        }

        // ==================== 接続 ====================

        private void RefreshConnectionList(Guid? selectId)
        {
            var allConns = ConnectionRegistry.Instance.Connections.ToList();
            ConnectionCombo.ItemsSource = allConns;

            if (allConns.Count == 0)
            {
                NoConnectionHint.Visibility = Visibility.Visible;
            }
            else
            {
                NoConnectionHint.Visibility = Visibility.Collapsed;
                var selected = selectId.HasValue
                    ? allConns.FirstOrDefault(c => c.Id == selectId.Value)
                    : null;
                ConnectionCombo.SelectedItem = selected ?? allConns[0];
            }
        }

        private void ConnectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 接続が変わったらテーブル一覧とプレビューをクリア
            TableCombo.ItemsSource = null;
            TableCombo.Text = "";
            PreviewGrid.ItemsSource = null;
            PreviewStatusText.Text = "";
        }

        private void ManageConnections_Click(object sender, RoutedEventArgs e)
        {
            var currentId = (ConnectionCombo.SelectedItem as DbConnectionInfo)?.Id;
            var dialog = new ConnectionManagerDialog { Owner = this };
            dialog.ShowDialog();
            RefreshConnectionList(currentId);
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionCombo.SelectedItem is not DbConnectionInfo conn)
            {
                MessageBox.Show("接続を選択してください。", "接続テスト",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (conn.DbType == DbType.Oracle)
                {
                    using var c = new OracleConnection(conn.ConnectionString);
                    await c.OpenAsync();
                }
                else
                {
                    using var c = new MySqlConnection(conn.ConnectionString);
                    await c.OpenAsync();
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

        // ==================== テーブル一覧取得 ====================

        private async void LoadTables_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionCombo.SelectedItem is not DbConnectionInfo conn)
            {
                MessageBox.Show("接続を選択してください。", "テーブル一覧取得",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LoadTablesButton.IsEnabled = false;
            LoadTablesButton.Content = "取得中...";

            try
            {
                var tables = await GetTablesAsync(conn);
                TableCombo.ItemsSource = tables;
                if (tables.Count > 0) TableCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"テーブル一覧の取得に失敗しました:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadTablesButton.IsEnabled = true;
                LoadTablesButton.Content = "↓ 一覧取得";
            }
        }

        private static async Task<List<string>> GetTablesAsync(DbConnectionInfo conn)
        {
            var tables = new List<string>();

            if (conn.DbType == DbType.Oracle)
            {
                using var c = new OracleConnection(conn.ConnectionString);
                await c.OpenAsync();
                using var cmd = new OracleCommand(
                    "SELECT table_name FROM user_tables ORDER BY table_name", c);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));
            }
            else
            {
                using var c = new MySqlConnection(conn.ConnectionString);
                await c.OpenAsync();
                using var cmd = new MySqlCommand(
                    "SELECT table_name FROM information_schema.tables " +
                    "WHERE table_schema = DATABASE() ORDER BY table_name", c);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));
            }

            return tables;
        }

        // ==================== SQL 自動生成 ====================

        private void GenerateSQL_Click(object sender, RoutedEventArgs e)
        {
            var tableName = TableCombo.Text?.Trim();
            if (string.IsNullOrEmpty(tableName))
            {
                MessageBox.Show("テーブルを選択または入力してください。", "SQL 自動生成",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SQLBox.Text = $"SELECT *\nFROM {tableName}";
            SQLBox.Focus();
            SQLBox.CaretIndex = SQLBox.Text.Length;
        }

        // ==================== プレビュー ====================

        private async void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionCombo.SelectedItem is not DbConnectionInfo conn)
            {
                MessageBox.Show("接続を選択してください。", "プレビュー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sql = SQLBox.Text.Trim();
            if (string.IsNullOrEmpty(sql))
            {
                MessageBox.Show("SQL クエリを入力してください。", "プレビュー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int limit = int.TryParse(LimitTextBox.Text, out var l) && l > 0 ? l : 100;
            LimitTextBox.Text = limit.ToString();

            PreviewButton.IsEnabled = false;
            PreviewGrid.ItemsSource = null;
            PreviewStatusText.Text = "実行中...";
            PreviewStatusText.Foreground = System.Windows.Media.Brushes.Gray;

            try
            {
                var dt = await ExecutePreviewAsync(conn, sql, limit);
                PreviewGrid.ItemsSource = dt.DefaultView;
                PreviewStatusText.Text = $"{dt.Rows.Count} 行取得" +
                    (dt.Rows.Count >= limit ? $"（最大 {limit} 行で打ち切り）" : "");
                PreviewStatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            }
            catch (Exception ex)
            {
                PreviewStatusText.Text = $"エラー: {ex.Message}";
                PreviewStatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                PreviewButton.IsEnabled = true;
            }
        }

        private static async Task<DataTable> ExecutePreviewAsync(DbConnectionInfo conn, string sql, int limit)
        {
            var dt = new DataTable();

            if (conn.DbType == DbType.Oracle)
            {
                using var c = new OracleConnection(conn.ConnectionString);
                await c.OpenAsync();
                using var cmd = new OracleCommand(sql, c);
                using var reader = await cmd.ExecuteReaderAsync();

                for (int i = 0; i < reader.FieldCount; i++)
                    dt.Columns.Add(reader.GetName(i));

                int count = 0;
                while (await reader.ReadAsync() && count < limit)
                {
                    var row = dt.NewRow();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                    dt.Rows.Add(row);
                    count++;
                }
            }
            else
            {
                using var c = new MySqlConnection(conn.ConnectionString);
                await c.OpenAsync();
                using var cmd = new MySqlCommand(sql, c);
                using var reader = await cmd.ExecuteReaderAsync();

                for (int i = 0; i < reader.FieldCount; i++)
                    dt.Columns.Add(reader.GetName(i));

                int count = 0;
                while (await reader.ReadAsync() && count < limit)
                {
                    var row = dt.NewRow();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                    dt.Rows.Add(row);
                    count++;
                }
            }

            return dt;
        }

        // ==================== OK / キャンセル ====================

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(StepNameBox.Text))
            {
                MessageBox.Show("ステップ名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ConnectionCombo.SelectedItem is not DbConnectionInfo conn)
            {
                MessageBox.Show("DB接続を選択してください。\n[接続設定管理] で接続を追加してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(SQLBox.Text))
            {
                MessageBox.Show("SQLクエリを入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName = StepNameBox.Text.Trim();
            ConnectionId = conn.Id;
            SQL = SQLBox.Text.Trim();
            ExecuteEachRow = ExecuteEachRowCheck.IsChecked == true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
