using System.Text;
using System.Windows;
using SampleELT.Models;

namespace SampleELT
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Shift-JIS などの追加エンコーディングを有効化
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ConnectionRegistry.Instance.Load();
        }
    }
}
