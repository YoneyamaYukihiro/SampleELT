using System.Windows;

namespace SampleELT.Dialogs
{
    public partial class DummyStepDialog : Window
    {
        public string StepName { get; private set; } = "";

        public DummyStepDialog() { InitializeComponent(); }

        public void Initialize(string stepName)
        {
            StepNameBox.Text = stepName;
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
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
