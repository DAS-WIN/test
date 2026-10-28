using SrbMinerMonitor.App.Services;

namespace SrbMinerMonitor.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (SrbMinerMonitor.Core.ConsoleStopSignal.TryHandleArguments(args, out var helperExit)) return helperExit;
        StartupDiagnostics.SuppressDialogs = args.Contains("--no-error-dialog");
        StartupDiagnostics.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            StartupDiagnostics.ReportFatal(e.ExceptionObject as Exception ?? new Exception("Unhandled runtime error."));
        TaskScheduler.UnobservedTaskException += (_, e) => StartupDiagnostics.Write("Unobserved task error: " + e.Exception);
        try
        {
            var app = new App();
            app.DispatcherUnhandledException += (_, e) =>
            {
                StartupDiagnostics.ReportFatal(e.Exception);
                e.Handled = true;
                app.Shutdown(1);
            };
            app.InitializeComponent();
            StartupDiagnostics.Write("Application resources initialized.");
            return app.Run();
        }
        catch (Exception e)
        {
            StartupDiagnostics.ReportFatal(e);
            return 1;
        }
    }
}
