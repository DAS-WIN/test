using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using SrbMinerMonitor.App;
using SrbMinerMonitor.App.Services;
using SrbMinerMonitor.App.ViewModels;

namespace SrbMinerMonitor.WindowsSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/screenshots");
        Directory.CreateDirectory(output);
        StartupDiagnostics.SuppressDialogs = true;
        StartupDiagnostics.Initialize();
        VerifyDefaultRunBindingRejectsReadOnlySource();
        var errors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var app = new SrbMinerMonitor.App.App();
        app.InitializeComponent();
        // Initialize resources without invoking App.OnStartup, which would open a second window.
        var window = new MainWindow(true);
        var vm = (MainViewModel)window.DataContext;
        var result = 0;
        window.Loaded += async (_, _) =>
        {
            try
            {
                var readyBy = DateTimeOffset.Now.AddSeconds(10);
                while ((!vm.IsDemo || vm.Gpus.Count == 0) && DateTimeOffset.Now < readyBy) await Task.Delay(50);
                if (!vm.IsDemo || vm.Gpus.Count != 1 || vm.Hashrate == "—") throw new InvalidOperationException("Demo initialization failed.");
                await Render("overview");
                var readOnlyRuns = FindReadOnlyRuns(window).ToArray();
                if (readOnlyRuns.Length != 2 || readOnlyRuns.Any(b => b.Mode != BindingMode.OneWay))
                    throw new InvalidOperationException("Read-only LastUpdate and Uptime Run.Text bindings must explicitly use OneWay.");
                vm.ScheduleCommand.Execute(null); await Render("schedule"); vm.CancelScheduleCommand.Execute(null);
                vm.SettingsCommand.Execute(null); await Render("settings");
                vm.EventsCommand.Execute(null); await Render("events");
                window.Width = 1100; window.Height = 720; vm.OverviewCommand.Execute(null); await Render("overview-small");
                if (errors.Messages.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Messages));
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: WPF loaded; read-only Run.Text bindings are OneWay; demo data, navigation, schedule panel, 1440x900 and 1100x720 rendered; no binding errors.");
            }
            catch (Exception e)
            {
                result = 1;
                File.WriteAllText(Path.Combine(output, "result.txt"), e.ToString());
                Console.Error.WriteLine(e);
            }
            finally
            {
                var exited = new TaskCompletionSource();
                vm.ExitRequested += () => exited.TrySetResult();
                vm.ExitCommand.Execute(null);
                await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
        };
        // App.OnStartup must not run: show directly and use a Dispatcher frame instead of Application.Run.
        var frame = new DispatcherFrame();
        window.Closed += (_, _) => frame.Continue = false;
        window.Show();
        Dispatcher.PushFrame(frame);
        return result;

        async Task Render(string name)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
        }
    }
    private static void VerifyDefaultRunBindingRejectsReadOnlySource()
    {
        // Reproduce the 0.1.0 startup failure before subscribing to binding-error traces.
        var source = new ReadOnlyText();
        try { new Run().SetBinding(Run.TextProperty, new Binding(nameof(ReadOnlyText.Value)) { Source = source }); }
        catch (InvalidOperationException)
        {
            var corrected = new Run();
            corrected.SetBinding(Run.TextProperty, new Binding(nameof(ReadOnlyText.Value)) { Source = source, Mode = BindingMode.OneWay });
            if (corrected.Text != source.Value) throw new InvalidOperationException("OneWay Run binding did not render the read-only value.");
            return;
        }
        throw new InvalidOperationException("Expected WPF's default TwoWay Run binding to reject a read-only property.");
    }
    private sealed class ReadOnlyText { public string Value => "startup regression"; }
    private static IEnumerable<Binding> FindReadOnlyRuns(DependencyObject parent)
    {
        if (parent is TextBlock text)
            foreach (var run in text.Inlines.OfType<Run>())
                if (BindingOperations.GetBinding(run, Run.TextProperty) is Binding binding && binding.Path?.Path is "LastUpdate" or "Uptime") yield return binding;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var binding in FindReadOnlyRuns(VisualTreeHelper.GetChild(parent, i))) yield return binding;
    }
    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrEmpty(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
