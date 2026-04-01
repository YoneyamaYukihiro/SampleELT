using System.Windows;

namespace SampleELT.Dialogs
{
    public partial class SelectValuesDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string FieldMappings { get; private set; } = "";

        public SelectValuesDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, string fieldMappings)
        {
            StepNameBox.Text = stepName;
            MappingsBox.Text = fieldMappings;
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
            FieldMappings = MappingsBox.Text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
