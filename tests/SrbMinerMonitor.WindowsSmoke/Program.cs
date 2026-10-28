using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SrbMinerMonitor.Core;
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
        var window = new MainWindow(true, new SettingsStore(Path.Combine(output, "isolated-settings")));
        var vm = (MainViewModel)window.DataContext;
        var result = 0;
        window.Loaded += async (_, _) =>
        {
            try
            {
                var readyBy = DateTimeOffset.Now.AddSeconds(10);
                while ((!vm.IsDemo || vm.Gpus.Count == 0) && DateTimeOffset.Now < readyBy) await Task.Delay(50);
                if (!vm.IsDemo || vm.Gpus.Count != 1 || vm.Hashrate == "—") throw new InvalidOperationException("Demo initialization failed.");
                if (window.Background is not SolidColorBrush rootBrush || rootBrush.Color != Color.FromRgb(16, 23, 32)) throw new InvalidOperationException("Window must explicitly use the dark background.");
                if (window.FontSize != 13) throw new InvalidOperationException("MainWindow must explicitly apply its base Window style.");
                await Render("overview");
                var readOnlyRuns = FindReadOnlyRuns(window).ToArray();
                if (readOnlyRuns.Length != 2 || readOnlyRuns.Any(b => b.Mode != BindingMode.OneWay))
                    throw new InvalidOperationException("Read-only LastUpdate and Uptime Run.Text bindings must explicitly use OneWay.");
                vm.ScheduleCommand.Execute(null); await Render("schedule"); vm.CancelScheduleCommand.Execute(null);
                vm.SettingsCommand.Execute(null); await Render("settings");
                var selector = Descendants<ComboBox>(window).Single(c => c.Name == "AlgorithmSelector");
                if (selector.IsEditable) throw new InvalidOperationException("Algorithm selector must not accept free text.");
                foreach (var title in Descendants<TextBlock>(window).Where(t => t.Style == (Style)window.FindResource("SectionTitle")))
                    if (title.Foreground is not SolidColorBrush brush || brush.Color != Color.FromRgb(231, 239, 246)) throw new InvalidOperationException("Section titles must inherit the light foreground.");
                if (args.Length > 1)
                {
                    await VerifyProbeFeedback(vm, args[1]); await Render("probe-results");
                    await VerifyBatchLifecycle(vm, args[1], output, Render);
                }
                vm.EventsCommand.Execute(null); await Render("events");
                window.Width = 1100; window.Height = 720; vm.OverviewCommand.Execute(null); await Render("overview-small");
                if (errors.Messages.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Messages));
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: explicit dark background and light titles; read-only algorithm selection; probe success/empty/failure/timeout with visible output; user-shaped BAT monitoring copy; BAT launch, real loopback HTTP polling, stop, restart and detached child ownership using a non-mining fixture; Run.Text OneWay; three pages, schedule, 1440x900/1100x720 rendered; no binding errors.");
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
    private static async Task Execute(MainViewModel vm, ICommand command, int seconds = 20)
    {
        if (!command.CanExecute(null)) throw new InvalidOperationException("Expected command to be available: " + vm.StartRequirement);
        command.Execute(null);
        var until = DateTimeOffset.Now.AddSeconds(seconds);
        while ((vm.IsBusy || command is AsyncCommand { IsExecuting: true }) && DateTimeOffset.Now < until) await Task.Delay(50);
        if (vm.IsBusy || command is AsyncCommand { IsExecuting: true }) throw new TimeoutException("UI command did not complete.");
    }
    private static async Task VerifyProbeFeedback(MainViewModel vm, string executable)
    {
        vm.Settings.Executable = executable;
        await Execute(vm, vm.ReadAlgorithmsCommand);
        if (vm.Settings.Algorithms.Count != 2 || !vm.ProbeStatus.StartsWith("完成") || !vm.ProbeDetails.Contains("退出碼：0")) throw new InvalidOperationException("Algorithm success feedback failed.");
        await Execute(vm, vm.DetectDevicesCommand);
        if (vm.Settings.Devices.Count != 2 || !vm.ProbeStatus.Contains("2 張 GPU")) throw new InvalidOperationException("Device detection feedback failed.");
        try
        {
            foreach (var mode in new[] { "empty", "fail", "slow" })
            {
                Environment.SetEnvironmentVariable("SRBM_FAKE_PROBE_MODE", mode);
                if (!vm.ReadAlgorithmsCommand.CanExecute(null)) throw new InvalidOperationException("Read-only probes must remain available in demo mode.");
                vm.ReadAlgorithmsCommand.Execute(null);
                if (mode == "slow" && (!vm.ReadingAlgorithms || !vm.ProbeStatus.Contains("執行中"))) throw new InvalidOperationException("No progress shown while the probe is running.");
                var until = DateTimeOffset.Now.AddSeconds(15);
                while ((vm.IsBusy || vm.ReadAlgorithmsCommand is AsyncCommand { IsExecuting: true }) && DateTimeOffset.Now < until) await Task.Delay(50);
                if (vm.IsBusy || vm.Settings.Algorithms.Count != 2 || !vm.ProbeStatus.StartsWith("失敗")) throw new InvalidOperationException("Probe failure did not preserve cache or show feedback: " + mode);
                if (mode == "empty" && !vm.ProbeDetails.Contains("未輸出任何資料")) throw new InvalidOperationException("Empty output is ambiguous.");
                if (mode == "fail" && !vm.ProbeDetails.Contains("driver unavailable")) throw new InvalidOperationException("stderr was not shown.");
                if (mode == "slow" && !vm.ProbeDetails.Contains("partial output")) throw new InvalidOperationException("Timeout discarded partial output.");
            }
        }
        finally { Environment.SetEnvironmentVariable("SRBM_FAKE_PROBE_MODE", null); }
        await Execute(vm, vm.ReadAlgorithmsCommand);
    }
    private static async Task VerifyBatchLifecycle(MainViewModel vm, string executable, string output, Func<string, Task> render)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SRBM-BAT-" + Guid.NewGuid().ToString("N"), "BAT 測試 & %SRBM_UNEXPANDED% !");
        Directory.CreateDirectory(directory);
        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(executable)!)) File.Copy(path, Path.Combine(directory, Path.GetFileName(path)), true);
        File.Copy(executable, Path.Combine(directory, "SRBMiner-MULTI.exe"), true);
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        const string userShape = "@echo off\r\nsetlocal\r\ncd /d \"%~dp0\"\r\nif not exist \"SRBMiner-MULTI.exe\" (\r\n echo ERROR: missing SRBMiner-MULTI.exe\r\n exit /b 1\r\n)\r\n\"%~dp0SRBMiner-MULTI.exe\" --algorithm-gpu pearlhash --disable-cpu --gpu-disable-oc --gpu-off-temperature 80 --pool fixture.example:8048 --tls true --wallet TEST --worker TEA-PC --log-file \"logs\\fixture.log\" --log-file-mode 1\r\npause\r\nendlocal\r\n";
        var original = Path.Combine(directory, "original.bat"); var monitoring = Path.Combine(directory, "original.monitor.bat");
        await File.WriteAllTextAsync(original, userShape, Encoding.ASCII);
        await File.WriteAllBytesAsync(monitoring, BatchFileInspector.CreateMonitoringCopy(await File.ReadAllBytesAsync(original), port));
        if (await File.ReadAllTextAsync(original) != userShape) throw new InvalidOperationException("Original BAT changed.");
        await Execute(vm, vm.SwitchLiveCommand);
        if (vm.IsDemo) throw new InvalidOperationException("Switch to real monitoring failed.");
        vm.Settings.UseBatchFile = true; vm.Settings.BatchFile = Path.GetFullPath(monitoring); vm.Settings.ApiPort = port.ToString();
        try
        {
            await Execute(vm, vm.SaveRestartCommand);
            await WaitForApi(vm);
            if (!vm.ApiStatus.StartsWith("API 已連線") || vm.Hashrate == "—" || vm.Gpus.Count != 1 || !vm.Status.StartsWith("BAT 運行中")) throw new InvalidOperationException("BAT -> API -> dashboard failed: " + vm.Notice + " / " + vm.ApiStatus);
            await render("bat-settings"); vm.OverviewCommand.Execute(null); await render("bat-live-api-fixture");
            Console.WriteLine("PASS: user-shaped BAT monitoring copy -> API -> dashboard.");
            await Execute(vm, vm.StopCommand);
            await AssertPortClosed(port);
            Console.WriteLine("PASS: managed BAT stop closed its API port.");
            // START exits its cmd parent immediately. The Job must still own the surviving child.
            var detached = Path.Combine(directory, "detached.bat");
            await File.WriteAllTextAsync(detached, $"@echo off\r\nstart \"\" /b \"%~dp0SRBMiner-MULTI.exe\" --api-enable --api-port {port}\r\nexit /b\r\n", Encoding.ASCII);
            vm.Settings.BatchFile = Path.GetFullPath(detached);
            await Execute(vm, vm.SaveRestartCommand);
            await WaitForApi(vm);
            await Task.Delay(1000);
            if (!vm.Status.StartsWith("BAT 運行中")) throw new InvalidOperationException("Lost ownership when cmd exited before its child.");
            Console.WriteLine("PASS: detached START child remains owned after cmd exits.");
            await Execute(vm, vm.RestartCommand);
            Console.WriteLine("Restart result: " + vm.Status + " / " + vm.Notice + " / " + vm.ApiStatus);
            await WaitForApi(vm);
            if (!vm.ApiStatus.StartsWith("API 已連線")) throw new InvalidOperationException("BAT restart failed.");
            await Execute(vm, vm.StopCommand);
            await AssertPortClosed(port);
            Console.WriteLine("PASS: detached BAT restart and stop completed; API port closed.");
            await File.WriteAllTextAsync(Path.Combine(output, "batch-integration.txt"), "PASS: original BAT preserved; monitoring copy adds API; Unicode/space/ampersand/literal-percent/exclamation paths; API -> dashboard; stop kills owned tree; detached START children remain owned; restart returns to API; no mining performed.");
        }
        finally
        {
            if (vm.StopCommand.CanExecute(null)) await Execute(vm, vm.StopCommand);
            vm.Settings.UseBatchFile = false;
            try { Directory.Delete(directory, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { StartupDiagnostics.Write("Deferred temporary fixture cleanup: " + e.Message); }
        }
    }
    private static async Task AssertPortClosed(int port)
    {
        using var client = new TcpClient();
        try { await client.ConnectAsync(IPAddress.Loopback, port); }
        catch (SocketException) { return; }
        throw new InvalidOperationException("Owned BAT child is still listening after stop.");
    }
    private static async Task WaitForApi(MainViewModel vm)
    {
        var until = DateTimeOffset.Now.AddSeconds(12);
        while (!vm.ApiStatus.StartsWith("API 已連線") && vm.Status.StartsWith("BAT 運行中") && DateTimeOffset.Now < until) await Task.Delay(50);
        if (!vm.ApiStatus.StartsWith("API 已連線") || !vm.Status.StartsWith("BAT 運行中")) throw new InvalidOperationException("API not ready: " + vm.Status + " / " + vm.Notice + " / " + vm.ApiStatus);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
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
