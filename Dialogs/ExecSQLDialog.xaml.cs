using System;
using System.Linq;
using System.Windows;
using MySqlConnector;
using Oracle.ManagedDataAccess.Client;
using SampleELT.Models;

namespace SampleELT.Dialogs
{
    public partial class ExecSQLDialog : Window
    {
        public string StepName { get; private set; } = "";
        public Guid? ConnectionId { get; private set; }
        public string SQL { get; private set; } = "";
        public bool ExecuteEachRow { get; private set; }

        public ExecSQLDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, Guid? connectionId, string sql, bool executeEachRow)
        {
            StepNameBox.Text = stepName;
            SQLBox.Text = sql;
            ExecuteEachRowCheck.IsChecked = executeEachRow;
            RefreshConnectionList(connectionId);
        }

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
                MessageBox.Show("SQLを入力してください。", "入力エラー",
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
