using System.Windows;
using System.Windows.Controls;

namespace SampleELT.Dialogs
{
    public partial class CalculationDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string OutputFieldName { get; private set; } = "Result";
        public string ExpressionType { get; private set; } = "add";
        public string Field1 { get; private set; } = "";
        public string Field2 { get; private set; } = "";
        public string Constant { get; private set; } = "0";

        public CalculationDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, string outputFieldName, string expressionType,
            string field1, string field2, string constant)
        {
            StepNameBox.Text = stepName;
            OutputFieldNameBox.Text = string.IsNullOrEmpty(outputFieldName) ? "Result" : outputFieldName;
            Field1Box.Text = field1;
            Field2Box.Text = field2;
            ConstantBox.Text = string.IsNullOrEmpty(constant) ? "0" : constant;

            foreach (ComboBoxItem item in ExpressionTypeCombo.Items)
            {
                if (item.Content?.ToString() == expressionType)
                {
                    ExpressionTypeCombo.SelectedItem = item;
                    break;
                }
            }

            UpdatePanelVisibility(expressionType);
        }

        private void ExpressionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (ExpressionTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "add";
            UpdatePanelVisibility(selected);
        }

        private void UpdatePanelVisibility(string expressionType)
        {
            if (Field2Panel == null || ConstantPanel == null) return;

            if (expressionType == "constant")
            {
                Field2Panel.Visibility = Visibility.Collapsed;
                ConstantPanel.Visibility = Visibility.Visible;
            }
            else
            {
                Field2Panel.Visibility = Visibility.Visible;
                ConstantPanel.Visibility = Visibility.Collapsed;
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

            if (string.IsNullOrWhiteSpace(OutputFieldNameBox.Text))
            {
                MessageBox.Show("出力フィールド名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName = StepNameBox.Text.Trim();
            OutputFieldName = OutputFieldNameBox.Text.Trim();
            ExpressionType = (ExpressionTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "add";
            Field1 = Field1Box.Text?.Trim() ?? "";
            Field2 = Field2Box.Text?.Trim() ?? "";
            Constant = ConstantBox.Text?.Trim() ?? "0";
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
