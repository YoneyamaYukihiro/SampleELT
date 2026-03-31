using System.Windows;
using Microsoft.Win32;

namespace SampleELT.Dialogs
{
    public partial class ExcelOutputDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string FilePath { get; private set; } = "";
        public string SheetName { get; private set; } = "Sheet1";
        public bool IncludeHeader { get; private set; } = true;

        public ExcelOutputDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, string filePath, string sheetName, bool includeHeader)
        {
            StepNameBox.Text = stepName;
            FilePathBox.Text = filePath;
            SheetNameBox.Text = string.IsNullOrEmpty(sheetName) ? "Sheet1" : sheetName;
            IncludeHeaderCheck.IsChecked = includeHeader;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Excelファイルの保存先を選択",
                Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                DefaultExt = "xlsx"
            };

            if (dialog.ShowDialog() == true)
            {
                FilePathBox.Text = dialog.FileName;
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

            if (string.IsNullOrWhiteSpace(FilePathBox.Text))
            {
                MessageBox.Show("ファイルパスを入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName = StepNameBox.Text.Trim();
            FilePath = FilePathBox.Text.Trim();
            SheetName = string.IsNullOrWhiteSpace(SheetNameBox.Text) ? "Sheet1" : SheetNameBox.Text.Trim();
            IncludeHeader = IncludeHeaderCheck.IsChecked == true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
