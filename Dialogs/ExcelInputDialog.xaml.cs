using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OfficeOpenXml;

namespace SampleELT.Dialogs
{
    public partial class ExcelInputDialog : Window
    {
        public string StepName  { get; private set; } = "";
        public string FilePath  { get; private set; } = "";
        public string SheetName { get; private set; } = "";
        public bool   HasHeader { get; private set; } = true;
        public string Format    { get; private set; } = "Excel";
        public string Delimiter { get; private set; } = ",";
        public string Encoding  { get; private set; } = "UTF-8";

        public ExcelInputDialog() { InitializeComponent(); }

        public void Initialize(string stepName, string filePath, string sheetName,
                               bool hasHeader, string format = "Excel",
                               string delimiter = ",", string encoding = "UTF-8")
        {
            StepNameBox.Text = stepName;
            FilePathBox.Text = filePath;
            HasHeaderCheck.IsChecked = hasHeader;

            // フォーマット選択
            foreach (ComboBoxItem item in FormatCombo.Items)
            {
                if ((item.Tag as string) == format) { FormatCombo.SelectedItem = item; break; }
            }

            // シート名 (Excel)
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath) && format == "Excel")
                LoadSheetNames(filePath);
            if (!string.IsNullOrEmpty(sheetName))
                SheetNameCombo.Text = sheetName;

            // 区切り文字
            bool delimiterSet = false;
            foreach (ComboBoxItem item in DelimiterCombo.Items)
            {
                var tag = item.Tag as string ?? "";
                if (tag != "custom" && tag == delimiter)
                {
                    DelimiterCombo.SelectedItem = item;
                    delimiterSet = true;
                    break;
                }
            }
            if (!delimiterSet)
            {
                // カスタム
                foreach (ComboBoxItem item in DelimiterCombo.Items)
                    if ((item.Tag as string) == "custom") { DelimiterCombo.SelectedItem = item; break; }
                CustomDelimiterBox.Text = delimiter;
            }

            // エンコーディング
            foreach (ComboBoxItem item in EncodingCombo.Items)
            {
                if ((item.Tag as string) == encoding) { EncodingCombo.SelectedItem = item; break; }
            }
        }

        private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SheetNameSection == null) return; // XAML 初期化前

            var fmt = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Excel";
            bool isExcel = fmt == "Excel";

            SheetNameSection.Visibility  = isExcel ? Visibility.Visible   : Visibility.Collapsed;
            DelimiterSection.Visibility  = isExcel ? Visibility.Collapsed : Visibility.Visible;
            EncodingSection.Visibility   = isExcel ? Visibility.Collapsed : Visibility.Visible;

            // TSV は区切り文字をタブに固定
            if (fmt == "TSV")
            {
                foreach (ComboBoxItem item in DelimiterCombo.Items)
                    if ((item.Tag as string) == "\t") { DelimiterCombo.SelectedItem = item; break; }
                DelimiterCombo.IsEnabled = false;
            }
            else
            {
                DelimiterCombo.IsEnabled = true;
            }
        }

        private void DelimiterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomDelimiterBox == null) return;
            var tag = (DelimiterCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            CustomDelimiterBox.Visibility = tag == "custom" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var fmt = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Excel";
            var filter = fmt switch
            {
                "CSV" => "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                "TSV" => "TSV files (*.tsv)|*.tsv|All files (*.*)|*.*",
                "TXT" => "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                _     => "Excel files (*.xlsx;*.xls)|*.xlsx;*.xls|All files (*.*)|*.*"
            };

            var dialog = new OpenFileDialog { Title = "ファイルを選択", Filter = filter };
            if (dialog.ShowDialog() == true)
            {
                FilePathBox.Text = dialog.FileName;
                if (fmt == "Excel") LoadSheetNames(dialog.FileName);
            }
        }

        private void FilePathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var path = FilePathBox.Text.Trim();
            var fmt = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Excel";
            if (fmt == "Excel" && File.Exists(path))
                LoadSheetNames(path);
        }

        private void LoadSheetNames(string filePath)
        {
            try
            {
                ExcelPackage.License.SetNonCommercialPersonal("SampleELT");
                SheetNameCombo.Items.Clear();
                using var package = new ExcelPackage(new FileInfo(filePath));
                foreach (var ws in package.Workbook.Worksheets)
                    SheetNameCombo.Items.Add(ws.Name);
                if (SheetNameCombo.Items.Count > 0)
                    SheetNameCombo.SelectedIndex = 0;
            }
            catch { }
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

            StepName  = StepNameBox.Text.Trim();
            FilePath  = FilePathBox.Text.Trim();
            SheetName = SheetNameCombo.Text?.Trim() ?? "";
            HasHeader = HasHeaderCheck.IsChecked == true;
            Format    = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Excel";

            var delimTag = (DelimiterCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? ",";
            Delimiter = delimTag == "custom" ? CustomDelimiterBox.Text : delimTag;
            if (string.IsNullOrEmpty(Delimiter)) Delimiter = ",";

            Encoding = (EncodingCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "UTF-8";

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
