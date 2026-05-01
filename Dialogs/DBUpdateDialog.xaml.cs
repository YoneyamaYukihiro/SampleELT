using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using SampleELT.Models;

namespace SampleELT.Dialogs
{
    public partial class DBUpdateDialog : Window
    {
        public string StepName { get; private set; } = "";
        public Guid? ConnectionId { get; private set; }
        public string TableName { get; private set; } = "";
        public string KeyFields { get; private set; } = "";
        public string UpdateFields { get; private set; } = "";
        public int CommitSize { get; private set; } = 100;

        private readonly List<ColumnItem> _columns = new();
        private string _initKeyFields = "";
        private string _initUpdateFields = "";

        public DBUpdateDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, Guid? connectionId, string tableName,
            string keyFields, string updateFields, int commitSize = 100)
        {
            StepNameBox.Text = stepName;
            CommitSizeBox.Text = commitSize.ToString();
            _initKeyFields = keyFields;
            _initUpdateFields = updateFields;
            // RefreshConnectionList を先に呼ぶことで SelectionChanged が空の TableCombo に対して発火し、
            // その後にセットした TableCombo.Text が上書きされるのを防ぐ
            RefreshConnectionList(connectionId);
            TableCombo.Text = tableName;

            // テーブル名が設定済みなら表示時にカラムを自動取得
            if (!string.IsNullOrEmpty(tableName))
                Loaded += async (_, _) => await AutoLoadColumnsAsync();
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
            TableCombo.ItemsSource = null;
            TableCombo.Text = "";
            ClearColumns();
        }

        private void ClearColumns()
        {
            _columns.Clear();
            ColumnGrid.ItemsSource = null;
            ColumnStatusText.Text = "※ カラム取得ボタンで一覧を表示できます";
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
                else if (conn.DbType == DbType.PostgreSQL)
                {
                    using var c = new NpgsqlConnection(conn.ConnectionString);
                    await c.OpenAsync();
                }
                else if (conn.DbType == DbType.SqlServer)
                {
                    using var c = new SqlConnection(conn.ConnectionString);
                    await c.OpenAsync();
                }
                else if (conn.DbType == DbType.Sqlite)
                {
                    using var c = new SqliteConnection(conn.ConnectionString);
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
                var currentText = TableCombo.Text;
                TableCombo.ItemsSource = tables;
                TableCombo.Text = currentText;
                if (string.IsNullOrEmpty(currentText) && tables.Count > 0)
                    TableCombo.SelectedIndex = 0;
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
            else if (conn.DbType == DbType.PostgreSQL)
            {
                using var c = new NpgsqlConnection(conn.ConnectionString);
                await c.OpenAsync();
                using var cmd = new NpgsqlCommand(
                    "SELECT table_name FROM information_schema.tables " +
                    "WHERE table_schema = current_schema() AND table_type = 'BASE TABLE' " +
                    "ORDER BY table_name", c);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));
            }
            else if (conn.DbType == DbType.SqlServer)
            {
                using var c = new SqlConnection(conn.ConnectionString);
                await c.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES " +
                    "WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME", c);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));
            }
            else if (conn.DbType == DbType.Sqlite)
            {
                using var c = new SqliteConnection(conn.ConnectionString);
                await c.OpenAsync();
                using var cmd = new SqliteCommand(
                    "SELECT name FROM sqlite_master WHERE type = 'table' " +
                    "AND name NOT LIKE 'sqlite_%' ORDER BY name", c);
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

        // ==================== カラム一覧取得 ====================

        private async void LoadColumns_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionCombo.SelectedItem is not DbConnectionInfo conn)
            {
                MessageBox.Show("接続を選択してください。", "カラム取得",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var tableName = TableCombo.Text?.Trim();
            if (string.IsNullOrEmpty(tableName))
            {
                MessageBox.Show("テーブル名を入力または選択してください。", "カラム取得",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LoadColumnsButton.IsEnabled = false;
            LoadColumnsButton.Content = "取得中...";
            await AutoLoadColumnsAsync();
            LoadColumnsButton.IsEnabled = true;
            LoadColumnsButton.Content = "🔄 カラム取得";
        }

        /// <summary>
        /// テーブルのカラム一覧を取得してグリッドに反映する。
        /// Initialize() の Loaded イベントと LoadColumns_Click の両方から呼ばれる。
        /// エラー時はエラーダイアログを出さず ColumnStatusText に表示する。
        /// </summary>
        private async Task AutoLoadColumnsAsync()
        {
            if (ConnectionCombo.SelectedItem is not DbConnectionInfo conn) return;
            var tableName = TableCombo.Text?.Trim();
            if (string.IsNullOrEmpty(tableName)) return;

            LoadColumnsButton.IsEnabled = false;
            ColumnStatusText.Text = "取得中...";
            try
            {
                var columnNames = await GetColumnsAsync(conn, tableName);
                var existingKeys    = ParseFields(_initKeyFields);
                var existingUpdates = ParseFields(_initUpdateFields);

                _columns.Clear();
                foreach (var name in columnNames)
                    _columns.Add(new ColumnItem
                    {
                        ColumnName = name,
                        IsKey    = existingKeys.Contains(name),
                        IsUpdate = existingUpdates.Contains(name)
                    });

                ColumnGrid.ItemsSource = null;
                ColumnGrid.ItemsSource = _columns;
                ColumnStatusText.Text = $"{columnNames.Count} カラム取得済み";
            }
            catch
            {
                ColumnStatusText.Text = "カラムの自動取得に失敗しました（カラム取得ボタンで再試行できます）";
            }
            finally
            {
                LoadColumnsButton.IsEnabled = true;
            }
        }

        private static async Task<List<string>> GetColumnsAsync(DbConnectionInfo conn, string tableName)
        {
            var columns = new List<string>();

            if (conn.DbType == DbType.Oracle)
            {
                using var c = new OracleConnection(conn.ConnectionString);
                await c.OpenAsync();
                using var cmd = new OracleCommand(
                    "SELECT column_name FROM user_tab_columns " +
                    "WHERE table_name = UPPER(:t) ORDER BY column_id", c);
                cmd.Parameters.Add(new OracleParameter(":t", tableName));
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(0));
            }
            else if (conn.DbType == DbType.PostgreSQL)
            {
                using var c = new NpgsqlConnection(conn.ConnectionString);
                await c.OpenAsync();
                using var cmd = new NpgsqlCommand(
                    "SELECT column_name FROM information_schema.columns " +
                    "WHERE table_schema = current_schema() AND table_name = @t ORDER BY ordinal_position", c);
                cmd.Parameters.AddWithValue("@t", tableName);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(0));
            }
            else if (conn.DbType == DbType.SqlServer)
            {
                using var c = new SqlConnection(conn.ConnectionString);
                await c.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                    "WHERE TABLE_NAME = @t ORDER BY ORDINAL_POSITION", c);
                cmd.Parameters.AddWithValue("@t", tableName);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(0));
            }
            else if (conn.DbType == DbType.Sqlite)
            {
                using var c = new SqliteConnection(conn.ConnectionString);
                await c.OpenAsync();
                // PRAGMA はパラメータをサポートしないためテーブル名を文字列リテラルとして組み込む
                var safeName = tableName.Replace("'", "''");
                using var cmd = new SqliteCommand($"PRAGMA table_info('{safeName}')", c);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(1)); // index 1 = name
            }
            else
            {
                using var c = new MySqlConnection(conn.ConnectionString);
                await c.OpenAsync();
                using var cmd = new MySqlCommand(
                    "SELECT column_name FROM information_schema.columns " +
                    "WHERE table_schema = DATABASE() AND table_name = @t ORDER BY ordinal_position", c);
                cmd.Parameters.AddWithValue("@t", tableName);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(0));
            }

            return columns;
        }

        private static HashSet<string> ParseFields(string csv) =>
            csv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
               .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

            if (string.IsNullOrWhiteSpace(TableCombo.Text))
            {
                MessageBox.Show("テーブル名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var keyFields = string.Join(",", _columns.Where(c => c.IsKey).Select(c => c.ColumnName));
            if (string.IsNullOrEmpty(keyFields))
            {
                MessageBox.Show("キーフィールドをカラム選択で指定してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName     = StepNameBox.Text.Trim();
            ConnectionId = conn.Id;
            TableName    = TableCombo.Text.Trim();
            KeyFields    = keyFields;
            UpdateFields = string.Join(",", _columns.Where(c => c.IsUpdate).Select(c => c.ColumnName));
            CommitSize   = int.TryParse(CommitSizeBox.Text.Trim(), out var cs) && cs >= 0 ? cs : 100;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        // ==================== ColumnItem ====================

        private class ColumnItem
        {
            public string ColumnName { get; set; } = "";
            public bool IsKey { get; set; }
            public bool IsUpdate { get; set; }
        }
    }
}
