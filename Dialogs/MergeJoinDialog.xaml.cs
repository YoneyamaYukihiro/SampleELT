using System.Windows;
using System.Windows.Controls;

namespace SampleELT.Dialogs
{
    public partial class MergeJoinDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string JoinType { get; private set; } = "INNER";
        public string KeyFields { get; private set; } = "";

        public MergeJoinDialog() { InitializeComponent(); }

        public void Initialize(string stepName, string joinType, string keyFields)
        {
            StepNameBox.Text = stepName;
            KeyFieldsBox.Text = keyFields;

            foreach (ComboBoxItem item in JoinTypeCombo.Items)
            {
                if (item.Content?.ToString() == joinType)
                {
                    JoinTypeCombo.SelectedItem = item;
                    break;
                }
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

            StepName = StepNameBox.Text.Trim();
            JoinType = (JoinTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "INNER";
            KeyFields = KeyFieldsBox.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
