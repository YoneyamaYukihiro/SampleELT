using System.Windows;

namespace SampleELT.Dialogs
{
    public partial class TableCompareDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string KeyFields { get; private set; } = "";
        public string CompareFields { get; private set; } = "";
        public bool NullsEqual { get; private set; } = true;
        public bool IgnoreCase { get; private set; }
        public bool TrimStrings { get; private set; }
        public bool IncludeMatched { get; private set; }

        public TableCompareDialog()
        {
            InitializeComponent();
        }

        public void Initialize(
            string stepName,
            string keyFields,
            string compareFields,
            bool nullsEqual,
            bool ignoreCase,
            bool trimStrings,
            bool includeMatched)
        {
            StepNameBox.Text          = stepName;
            KeyFieldsBox.Text         = keyFields;
            CompareFieldsBox.Text     = compareFields;
            NullsEqualCheck.IsChecked     = nullsEqual;
            IgnoreCaseCheck.IsChecked     = ignoreCase;
            TrimStringsCheck.IsChecked    = trimStrings;
            IncludeMatchedCheck.IsChecked = includeMatched;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(StepNameBox.Text))
            {
                MessageBox.Show("ステップ名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(KeyFieldsBox.Text))
            {
                MessageBox.Show("キーフィールドを入力してください (カンマ区切り)。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName       = StepNameBox.Text.Trim();
            KeyFields      = KeyFieldsBox.Text.Trim();
            CompareFields  = CompareFieldsBox.Text.Trim();
            NullsEqual     = NullsEqualCheck.IsChecked == true;
            IgnoreCase     = IgnoreCaseCheck.IsChecked == true;
            TrimStrings    = TrimStringsCheck.IsChecked == true;
            IncludeMatched = IncludeMatchedCheck.IsChecked == true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
