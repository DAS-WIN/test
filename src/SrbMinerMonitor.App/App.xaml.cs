using System.Windows;

namespace SrbMinerMonitor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(e.Args.Contains("--demo"));
        MainWindow = window;
        window.Show();
    }
}
