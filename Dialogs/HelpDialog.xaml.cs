using System.IO;
using System.Text;
using System.Windows;

namespace SampleELT.Dialogs
{
    public partial class HelpDialog : Window
    {
        public HelpDialog(int initialTab = 0)
        {
            InitializeComponent();
            HelpTabControl.SelectedIndex = initialTab;
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            UsageTextBox.Text = ReadDoc(Path.Combine(baseDir, "docs", "使い方.md"));
            SpecTextBox.Text  = ReadDoc(Path.Combine(baseDir, "docs", "仕様書.md"));
        }

        private static string ReadDoc(string path) =>
            File.Exists(path)
                ? File.ReadAllText(path, Encoding.UTF8)
                : $"({Path.GetFileName(path)} が見つかりません)";
    }
}
