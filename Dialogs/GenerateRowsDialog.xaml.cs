using System.Windows;

namespace SampleELT.Dialogs
{
    public partial class GenerateRowsDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string Fields { get; private set; } = "";
        public string RowCount { get; private set; } = "1";

        public GenerateRowsDialog() { InitializeComponent(); }

        public void Initialize(string stepName, string fields, string rowCount)
        {
            StepNameBox.Text = stepName;
            FieldsBox.Text = fields;
            RowCountBox.Text = string.IsNullOrEmpty(rowCount) ? "1" : rowCount;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(StepNameBox.Text))
            {
                MessageBox.Show("ステップ名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(RowCountBox.Text.Trim(), out var n) || n < 1)
            {
                MessageBox.Show("生成行数は1以上の整数を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName = StepNameBox.Text.Trim();
            Fields = FieldsBox.Text;
            RowCount = n.ToString();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
