using System.Windows;
using SampleELT.Models;

namespace SampleELT
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ConnectionRegistry.Instance.Load();
        }
    }
}
