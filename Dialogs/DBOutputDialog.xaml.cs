using System;
using System.Windows;
using System.Windows.Controls;
using MySqlConnector;
using Oracle.ManagedDataAccess.Client;

namespace SampleELT.Dialogs
{
    public partial class DBOutputDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string DBType { get; private set; } = "Oracle";
        public string ConnectionString { get; private set; } = "";
        public string TableName { get; private set; } = "";
        public string Mode { get; private set; } = "INSERT";

        public DBOutputDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, string dbType, string connectionString, string tableName, string mode)
        {
            StepNameBox.Text = stepName;
            ConnectionStringBox.Text = connectionString;
            TableNameBox.Text = tableName;

            // Set DBType combo
            foreach (ComboBoxItem item in DBTypeCombo.Items)
            {
                if (item.Content?.ToString() == dbType)
                {
                    DBTypeCombo.SelectedItem = item;
                    break;
                }
            }

            // Set Mode combo
            foreach (ComboBoxItem item in ModeCombo.Items)
            {
                if (item.Content?.ToString() == mode)
                {
                    ModeCombo.SelectedItem = item;
                    break;
                }
            }
        }

        private void DBTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Could update UI hints based on DB type selection
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            var cs = ConnectionStringBox.Text.Trim();
            if (string.IsNullOrEmpty(cs))
            {
                MessageBox.Show("接続文字列を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedDbType = (DBTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Oracle";

            try
            {
                if (selectedDbType == "MySQL")
                {
                    using var conn = new MySqlConnection(cs);
                    await conn.OpenAsync();
                }
                else
                {
                    using var conn = new OracleConnection(cs);
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

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(StepNameBox.Text))
            {
                MessageBox.Show("ステップ名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(ConnectionStringBox.Text))
            {
                MessageBox.Show("接続文字列を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(TableNameBox.Text))
            {
                MessageBox.Show("テーブル名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName = StepNameBox.Text.Trim();
            DBType = (DBTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Oracle";
            ConnectionString = ConnectionStringBox.Text.Trim();
            TableName = TableNameBox.Text.Trim();
            Mode = (ModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "INSERT";
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
