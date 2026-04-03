using System.Windows;
using System.Windows.Controls;

namespace SampleELT.Dialogs
{
    public partial class FilterDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string FieldName { get; private set; } = "";
        public string Operator { get; private set; } = "equals";
        public string Value { get; private set; } = "";
        public string RightField { get; private set; } = "";

        public FilterDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, string fieldName, string op, string value, string rightField = "")
        {
            StepNameBox.Text = stepName;
            FieldNameBox.Text = fieldName;
            ValueBox.Text = value;
            RightFieldBox.Text = rightField;

            foreach (ComboBoxItem item in OperatorCombo.Items)
            {
                if (item.Content?.ToString() == op)
                {
                    OperatorCombo.SelectedItem = item;
                    break;
                }
            }

            UpdateValueFieldState(op);
        }

        private void OperatorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedOp = (OperatorCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "equals";
            UpdateValueFieldState(selectedOp);
        }

        private void UpdateValueFieldState(string op)
        {
            if (ValueBox == null) return;
            bool isNullCheck = op == "isNull" || op == "isNotNull";
            ValueBox.IsEnabled = !isNullCheck;
            if (isNullCheck)
            {
                ValueBox.Text = "";
                ValueBox.Background = System.Windows.Media.Brushes.LightGray;
            }
            else
            {
                ValueBox.Background = System.Windows.Media.Brushes.White;
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

            if (string.IsNullOrWhiteSpace(FieldNameBox.Text))
            {
                MessageBox.Show("フィールド名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName = StepNameBox.Text.Trim();
            FieldName = FieldNameBox.Text.Trim();
            Operator = (OperatorCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "equals";
            Value = ValueBox.Text?.Trim() ?? "";
            RightField = RightFieldBox.Text?.Trim() ?? "";
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
