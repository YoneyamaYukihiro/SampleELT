using System;
using System.Windows;
using MySqlConnector;

namespace SampleELT.Dialogs
{
    public partial class MySQLInputDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string ConnectionString { get; private set; } = "";
        public string SQL { get; private set; } = "";

        public MySQLInputDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, string connectionString, string sql)
        {
            StepNameBox.Text = stepName;
            SQLBox.Text = sql;

            // Parse connection string if provided
            if (!string.IsNullOrEmpty(connectionString))
            {
                try
                {
                    var builder = new MySqlConnectionStringBuilder(connectionString);
                    ServerBox.Text = builder.Server;
                    PortBox.Text = builder.Port.ToString();
                    DatabaseBox.Text = builder.Database;
                    UsernameBox.Text = builder.UserID;
                    PasswordBox.Password = builder.Password;
                }
                catch
                {
                    // If parsing fails, leave defaults
                }
            }
        }

        private string BuildConnectionString()
        {
            var server = ServerBox.Text.Trim();
            var portStr = PortBox.Text.Trim();
            var database = DatabaseBox.Text.Trim();
            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;

            if (!uint.TryParse(portStr, out uint port)) port = 3306;

            var builder = new MySqlConnectionStringBuilder
            {
                Server = server,
                Port = port,
                Database = database,
                UserID = username,
                Password = password
            };

            return builder.ConnectionString;
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cs = BuildConnectionString();
                using var conn = new MySqlConnection(cs);
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

            if (string.IsNullOrWhiteSpace(DatabaseBox.Text))
            {
                MessageBox.Show("データベース名を入力してください。", "入力エラー",
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
            ConnectionString = BuildConnectionString();
            SQL = SQLBox.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
