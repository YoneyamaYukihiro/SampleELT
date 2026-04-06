using System.Windows;

namespace SampleELT.Dialogs
{
    public partial class SQLEditorDialog : Window
    {
        public string SQL { get; private set; } = "";

        public SQLEditorDialog()
        {
            InitializeComponent();
        }

        public void Initialize(string sql)
        {
            SQLBox.Text = sql;
            SQLBox.Focus();
            SQLBox.CaretIndex = SQLBox.Text.Length;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SQL = SQLBox.Text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
