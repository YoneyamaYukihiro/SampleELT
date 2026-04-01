using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using OfficeOpenXml;

namespace SampleELT.Dialogs
{
    public partial class ExcelInputDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string FilePath { get; private set; } = "";
        public string SheetName { get; private set; } = "";
        public bool HasHeader { get; private set; } = true;

        public ExcelInputDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string stepName, string filePath, string sheetName, bool hasHeader)
        {
            StepNameBox.Text = stepName;
            FilePathBox.Text = filePath;
            HasHeaderCheck.IsChecked = hasHeader;

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                LoadSheetNames(filePath);
            }

            if (!string.IsNullOrEmpty(sheetName))
            {
                SheetNameCombo.Text = sheetName;
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Excelファイルを選択",
                Filter = "Excel files (*.xlsx;*.xls)|*.xlsx;*.xls|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                FilePathBox.Text = dialog.FileName;
                LoadSheetNames(dialog.FileName);
            }
        }

        private void FilePathBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var path = FilePathBox.Text.Trim();
            if (File.Exists(path))
            {
                LoadSheetNames(path);
            }
        }

        private void LoadSheetNames(string filePath)
        {
            try
            {
                ExcelPackage.License.SetNonCommercialPersonal("SampleELT");
                SheetNameCombo.Items.Clear();

                using var package = new ExcelPackage(new FileInfo(filePath));
                foreach (var ws in package.Workbook.Worksheets)
                {
                    SheetNameCombo.Items.Add(ws.Name);
                }

                if (SheetNameCombo.Items.Count > 0)
                {
                    SheetNameCombo.SelectedIndex = 0;
                }
            }
            catch
            {
                // Silently ignore errors when loading sheet names
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
            SheetName = SheetNameCombo.Text?.Trim() ?? "";
            HasHeader = HasHeaderCheck.IsChecked == true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
