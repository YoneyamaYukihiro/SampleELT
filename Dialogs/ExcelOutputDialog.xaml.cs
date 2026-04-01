using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SampleELT.Dialogs
{
    public partial class ExcelOutputDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string FilePath { get; private set; } = "";
        public string Format { get; private set; } = "Excel";
        public string SheetName { get; private set; } = "Sheet1";
        public string Delimiter { get; private set; } = ",";
        public string Encoding { get; private set; } = "UTF-8";
        public bool IncludeHeader { get; private set; } = true;

        private bool _suppressEvents;

        public ExcelOutputDialog()
        {
            _suppressEvents = true;
            InitializeComponent();
            _suppressEvents = false;
        }

        public void Initialize(string stepName, string filePath, string format, string sheetName,
            string delimiter, string encoding, bool includeHeader)
        {
            _suppressEvents = true;

            StepNameBox.Text = stepName;
            FilePathBox.Text = filePath;
            SheetNameBox.Text = string.IsNullOrEmpty(sheetName) ? "Sheet1" : sheetName;
            IncludeHeaderCheck.IsChecked = includeHeader;

            // Set format
            foreach (ComboBoxItem item in FormatCombo.Items)
            {
                if (item.Tag?.ToString() == format)
                {
                    FormatCombo.SelectedItem = item;
                    break;
                }
            }

            // Set delimiter
            bool foundDelimiter = false;
            foreach (ComboBoxItem item in DelimiterCombo.Items)
            {
                if (item.Tag?.ToString() == "custom") continue;
                if (item.Tag?.ToString() == delimiter)
                {
                    DelimiterCombo.SelectedItem = item;
                    foundDelimiter = true;
                    break;
                }
            }
            if (!foundDelimiter && !string.IsNullOrEmpty(delimiter))
            {
                // Select "custom"
                foreach (ComboBoxItem item in DelimiterCombo.Items)
                {
                    if (item.Tag?.ToString() == "custom")
                    {
                        DelimiterCombo.SelectedItem = item;
                        CustomDelimiterBox.Text = delimiter;
                        CustomDelimiterBox.Visibility = Visibility.Visible;
                        break;
                    }
                }
            }

            // Set encoding
            foreach (ComboBoxItem item in EncodingCombo.Items)
            {
                if (item.Tag?.ToString() == encoding)
                {
                    EncodingCombo.SelectedItem = item;
                    break;
                }
            }

            _suppressEvents = false;
            UpdateFormatSections(format);
        }

        private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            var format = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Excel";
            UpdateFormatSections(format);

            // Update file extension hint
            if (!string.IsNullOrEmpty(FilePathBox.Text))
            {
                var path = FilePathBox.Text;
                var ext = format switch
                {
                    "CSV" => ".csv",
                    "TSV" => ".tsv",
                    "TXT" => ".txt",
                    _ => ".xlsx"
                };
                var newPath = System.IO.Path.ChangeExtension(path, ext);
                FilePathBox.Text = newPath;
            }
        }

        private void UpdateFormatSections(string format)
        {
            bool isExcel = format == "Excel";
            SheetNameSection.Visibility = isExcel ? Visibility.Visible : Visibility.Collapsed;
            DelimiterSection.Visibility = isExcel ? Visibility.Collapsed : Visibility.Visible;
            EncodingSection.Visibility = isExcel ? Visibility.Collapsed : Visibility.Visible;

            // TSV: lock delimiter to tab
            if (format == "TSV" && DelimiterCombo != null)
            {
                foreach (ComboBoxItem item in DelimiterCombo.Items)
                {
                    if (item.Tag?.ToString() == "\t")
                    {
                        DelimiterCombo.SelectedItem = item;
                        break;
                    }
                }
                DelimiterCombo.IsEnabled = false;
            }
            else if (DelimiterCombo != null)
            {
                DelimiterCombo.IsEnabled = true;
            }
        }

        private void DelimiterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents || CustomDelimiterBox == null) return;
            var tag = (DelimiterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            CustomDelimiterBox.Visibility = tag == "custom" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var format = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Excel";
            var (filter, ext) = format switch
            {
                "CSV" => ("CSV files (*.csv)|*.csv|All files (*.*)|*.*", "csv"),
                "TSV" => ("TSV files (*.tsv)|*.tsv|All files (*.*)|*.*", "tsv"),
                "TXT" => ("Text files (*.txt)|*.txt|All files (*.*)|*.*", "txt"),
                _ => ("Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*", "xlsx")
            };

            var dialog = new SaveFileDialog
            {
                Title = "出力ファイルの保存先を選択",
                Filter = filter,
                DefaultExt = ext,
                FileName = Path.GetFileName(FilePathBox.Text)
            };

            if (dialog.ShowDialog() == true)
                FilePathBox.Text = dialog.FileName;
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

            var delimTag = (DelimiterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ",";
            var resolvedDelimiter = delimTag == "custom"
                ? (string.IsNullOrEmpty(CustomDelimiterBox.Text) ? "," : CustomDelimiterBox.Text)
                : delimTag;

            StepName = StepNameBox.Text.Trim();
            FilePath = FilePathBox.Text.Trim();
            Format = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Excel";
            SheetName = string.IsNullOrWhiteSpace(SheetNameBox.Text) ? "Sheet1" : SheetNameBox.Text.Trim();
            Delimiter = resolvedDelimiter;
            Encoding = (EncodingCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "UTF-8";
            IncludeHeader = IncludeHeaderCheck.IsChecked == true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
