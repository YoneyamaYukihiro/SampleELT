using System.Windows;

namespace BreezeFlow.Dialogs
{
    public partial class SwitchDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string FieldName { get; private set; } = "";
        public string Cases { get; private set; } = "";
        public bool IncludeDefault { get; private set; } = true;

        public SwitchDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, string fieldName, string cases, bool includeDefault)
        {
            StepNameBox.Text = stepName;
            FieldNameBox.Text = fieldName;
            CasesBox.Text = cases;
            IncludeDefaultCheck.IsChecked = includeDefault;
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
                MessageBox.Show("評価フィールド名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName = StepNameBox.Text.Trim();
            FieldName = FieldNameBox.Text.Trim();
            Cases = CasesBox.Text?.Trim() ?? "";
            IncludeDefault = IncludeDefaultCheck.IsChecked == true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
