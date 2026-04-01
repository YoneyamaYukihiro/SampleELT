using System.Windows;
using System.Windows.Controls;

namespace SampleELT.Dialogs
{
    public partial class SetVariableDialog : Window
    {
        public string StepName   { get; private set; } = "";
        public string Fields     { get; private set; } = "";
        public string DateFormat { get; private set; } = "yyyy/MM/dd";

        public SetVariableDialog() { InitializeComponent(); }

        public void Initialize(string stepName, string fields, string dateFormat)
        {
            StepNameBox.Text = stepName;
            FieldsBox.Text   = fields;
            DateFormatCombo.Text = string.IsNullOrEmpty(dateFormat) ? "yyyy/MM/dd" : dateFormat;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(StepNameBox.Text))
            {
                MessageBox.Show("ステップ名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName   = StepNameBox.Text.Trim();
            Fields     = FieldsBox.Text;
            DateFormat = DateFormatCombo.Text?.Trim() ?? "yyyy/MM/dd";
            if (string.IsNullOrEmpty(DateFormat)) DateFormat = "yyyy/MM/dd";

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
