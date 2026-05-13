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
            UpdateJoinTypeDescription(joinType);
        }

        private void JoinTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (JoinTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "INNER";
            UpdateJoinTypeDescription(selected);
        }

        private void UpdateJoinTypeDescription(string joinType)
        {
            if (JoinTypeDescription == null) return;
            JoinTypeDescription.Text = joinType switch
            {
                "INNER"       => "左右両方でキーが一致する行のみ出力 (SQL INNER JOIN 相当)",
                "LEFT OUTER"  => "左の全行を出力。右に対応行が無ければ右側カラムは null",
                "RIGHT OUTER" => "右の全行を出力。左に対応行が無ければ左側カラムは null",
                "FULL OUTER"  => "左右両方の全行を出力。対応行が無い側のカラムは null",
                _             => ""
            };
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
