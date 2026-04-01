using System.Windows;

namespace SampleELT.Dialogs
{
    public partial class JavaScriptDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string Script { get; private set; } = "";
        public bool RunPerRow { get; private set; } = true;

        public JavaScriptDialog() { InitializeComponent(); }

        public void Initialize(string stepName, string script, bool runPerRow)
        {
            StepNameBox.Text = stepName;
            ScriptBox.Text = script;
            RunPerRowCheck.IsChecked = runPerRow;
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
            Script = ScriptBox.Text;
            RunPerRow = RunPerRowCheck.IsChecked == true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
