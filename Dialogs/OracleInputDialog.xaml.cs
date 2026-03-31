using System;
using System.Windows;
using Oracle.ManagedDataAccess.Client;

namespace SampleELT.Dialogs
{
    public partial class OracleInputDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string ConnectionString { get; private set; } = "";
        public string SQL { get; private set; } = "";

        public OracleInputDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, string connectionString, string sql)
        {
            StepNameBox.Text = stepName;
            ConnectionStringBox.Text = connectionString;
            SQLBox.Text = sql;
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

            try
            {
                using var conn = new OracleConnection(cs);
                await conn.OpenAsync();
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

            if (string.IsNullOrWhiteSpace(SQLBox.Text))
            {
                MessageBox.Show("SQLクエリを入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName = StepNameBox.Text.Trim();
            ConnectionString = ConnectionStringBox.Text.Trim();
            SQL = SQLBox.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
