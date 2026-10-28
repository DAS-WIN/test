using System.Windows;
using SrbMinerMonitor.App.Services;
using SrbMinerMonitor.App.ViewModels;

namespace SrbMinerMonitor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(e.Args.Contains("--demo"));
        StartupDiagnostics.Write("Main window constructed.");
        if (e.Args.Contains("--verify-startup"))
        {
            window.ContentRendered += (_, _) =>
            {
                StartupDiagnostics.Write("STARTUP VERIFICATION PASSED: main window rendered through the executable entry point.");
                ((MainViewModel)window.DataContext).ExitCommand.Execute(null);
            };
        }
        MainWindow = window;
        window.Show();
    }
}
