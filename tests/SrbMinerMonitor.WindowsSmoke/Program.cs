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
        var window = new MainWindow( new SettingsStore(Path.Combine(output, "isolated-settings")));
        var vm = (MainViewModel)window.DataContext;
        var result = 0;
        window.Loaded += async (_, _) =>
        {
            try
            {
                if (vm.Gpus.Count != 0 || vm.Hashrate != "—") throw new InvalidOperationException("Startup must be empty; no demonstration data or automatic mining.");
                if (Descendants<Button>(window).Any(b => b.Content?.ToString()?.Contains("示範") == true)) throw new InvalidOperationException("Demo UI remains.");
                if (window.Background is not SolidColorBrush rootBrush || rootBrush.Color != Color.FromRgb(16, 23, 32)) throw new InvalidOperationException("Window must explicitly use the dark background.");
                if (window.FontSize != 13) throw new InvalidOperationException("MainWindow must explicitly apply its base Window style.");
                await Render("overview");
                var readOnlyRuns = FindReadOnlyRuns(window).ToArray();
                if (readOnlyRuns.Length != 2 || readOnlyRuns.Any(b => b.Mode != BindingMode.OneWay))
                    throw new InvalidOperationException("Read-only LastUpdate and Uptime Run.Text bindings must explicitly use OneWay.");
                vm.ScheduleCommand.Execute(null); await Render("schedule"); vm.CancelScheduleCommand.Execute(null);
                vm.SettingsCommand.Execute(null); await Render("settings");
                if (Descendants<ComboBox>(window).Any(c => c.Name == "AlgorithmSelector")) throw new InvalidOperationException("Non-BAT settings remain.");
                var heading = Descendants<TextBlock>(window).Single(t => t.Name == "RecentSharesHeading");
                var cards = Descendants<System.Windows.Controls.Primitives.UniformGrid>(window).Single(g => g.Name == "SummaryCards");
                if (heading.TranslatePoint(new Point(), window).Y >= cards.TranslatePoint(new Point(), window).Y) throw new InvalidOperationException("Recent accepted must be above total hashrate.");
                if (Descendants<TextBlock>(window).Any(t => t.Text.Contains("成功分享 Accepted（全部 GPU）"))) throw new InvalidOperationException("Green accepted banner remains.");
                foreach (var title in Descendants<TextBlock>(window).Where(t => t.Style == (Style)window.FindResource("SectionTitle")))
                    if (title.Foreground is not SolidColorBrush brush || brush.Color != Color.FromRgb(231, 239, 246)) throw new InvalidOperationException("Section titles must inherit the light foreground.");
                if (args.Length > 1)
                {
                    await VerifyBatchLifecycle(vm, args[1], output, Render);
                }
                vm.EventsCommand.Execute(null); await Render("events");
                window.Width = 1100; window.Height = 720; vm.OverviewCommand.Execute(null); await Render("overview-small");
                if (errors.Messages.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Messages));
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: dark theme; no demo or non-BAT launch; accepted above hashrate and no banner; event log contains only success/failure/timeout; minute hashrate independent of live shares; existing and pasted BAT; protected settings; BAT launch, real loopback HTTP polling, stop, restart and detached child ownership using a non-mining fixture; Run.Text OneWay; three pages, schedule, 1440x900/1100x720 rendered; no binding errors.");
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
    private static async Task VerifyBatchLifecycle(MainViewModel vm, string executable, string output, Func<string, Task> render)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SRBM-BAT-" + Guid.NewGuid().ToString("N"), "BAT 測試 & %SRBM_UNEXPANDED% !");
        Directory.CreateDirectory(directory);
        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(executable)!)) File.Copy(path, Path.Combine(directory, Path.GetFileName(path)), true);
        File.Copy(executable, Path.Combine(directory, "SRBMiner-MULTI.exe"), true);
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        const string userShape = "@echo off\r\nsetlocal\r\ncd /d \"%~dp0\"\r\nif not exist \"SRBMiner-MULTI.exe\" (\r\n echo ERROR: missing SRBMiner-MULTI.exe\r\n exit /b 1\r\n)\r\n\"%~dp0SRBMiner-MULTI.exe\" --algorithm-gpu pearlhash --disable-cpu --gpu-disable-oc --gpu-off-temperature 80 --pool fixture.example:8048 --tls true --wallet TEST --worker TEA-PC --log-file \"logs\\fixture.log\" --log-file-mode 1 --fixture-no-share-api\r\npause\r\nendlocal\r\n";
        var original = Path.Combine(directory, "original.bat"); var monitoring = Path.Combine(directory, "original.monitor.bat");
        await File.WriteAllTextAsync(original, userShape, Encoding.ASCII);
        await File.WriteAllBytesAsync(monitoring, BatchFileInspector.CreateMonitoringCopy(await File.ReadAllBytesAsync(original), port));
        if (await File.ReadAllTextAsync(original) != userShape) throw new InvalidOperationException("Original BAT changed.");
        vm.Settings.BatchInputMode = vm.Settings.BatchModes[0]; vm.Settings.BatchFile = Path.GetFullPath(monitoring); vm.Settings.ApiPort = port.ToString();
        try
        {
            await Execute(vm, vm.SaveRestartCommand);
            await WaitForApi(vm);
            if (!vm.ApiStatus.StartsWith("API 已連線") || vm.Hashrate == "—" || vm.Gpus.Count != 1 || !vm.Status.StartsWith("BAT 運行中")) throw new InvalidOperationException("BAT -> API -> dashboard failed: " + vm.Notice + " / " + vm.ApiStatus);
            await render("bat-settings"); vm.OverviewCommand.Execute(null); await render("bat-live-api-fixture");
            if (vm.TotalAccepted != "4" || vm.Rejected != "1" || vm.Pool != "fixture.example:8048") throw new InvalidOperationException("Output shares or BAT pool missing: " + vm.TotalAccepted + " / " + vm.Pool);
            vm.SelectedGpu = vm.Gpus[0];
            if (vm.Accepted != "4" || vm.Acceptance != "80.0 %") throw new InvalidOperationException("Selected GPU hides accepted shares: " + vm.Accepted);
            if (!vm.MinerOutputText.Contains("FIXTURE_OUTPUT_REDIRECTED=True") || vm.MinerOutputText.Contains("FIXTURE_CONSOLE_VISIBLE=True") || !vm.MinerOutputText.Contains("FIXTURE_CONSOLE_VISIBLE=False")) throw new InvalidOperationException("Miner output must be redirected with its console hidden.");
            if (vm.RecentShares.Count() != 3 || vm.RecentShares.Any(e => !e.Message.Contains("share accepted"))) throw new InvalidOperationException("Green accepted events missing from application.");
            await render("accepted-background-miner");
            Console.WriteLine("PASS: user-shaped BAT -> hidden console -> captured output -> accepted=4, selected GPU=4, rejected=1, acceptance=80%, configured pool.");
            var beforeShare = vm.TotalAccepted; var beforeHash = vm.Hashrate; var beforeHistory = vm.History.Samples.Count;
            var beforeMinute = DateTimeOffset.UtcNow.UtcTicks / TimeSpan.TicksPerMinute;
            await File.WriteAllTextAsync(Path.Combine(directory, "fixture-accepted"), "go");
            var realtimeDeadline = DateTimeOffset.Now.AddSeconds(2);
            while (vm.TotalAccepted == beforeShare && DateTimeOffset.Now < realtimeDeadline) await Task.Delay(20);
            if (vm.TotalAccepted != "5") throw new InvalidOperationException("Accepted did not update in real time independently of API: " + vm.TotalAccepted);
            if (DateTimeOffset.UtcNow.UtcTicks / TimeSpan.TicksPerMinute == beforeMinute && (vm.Hashrate != beforeHash || vm.History.Samples.Count != beforeHistory)) throw new InvalidOperationException("Share update changed hashrate inside a minute.");
            await File.WriteAllTextAsync(Path.Combine(directory, "fixture-timeout"), "go");
            var timeoutDeadline = DateTimeOffset.Now.AddSeconds(2);
            while (!vm.Events.Any(e => e.Category == "逾時") && DateTimeOffset.Now < timeoutDeadline) await Task.Delay(20);
            if (!vm.Events.Any(e => e.Category == "成功") || !vm.Events.Any(e => e.Category == "失敗") || !vm.Events.Any(e => e.Category == "逾時") || vm.Events.Any(e => e.Category is not ("成功" or "失敗" or "逾時"))) throw new InvalidOperationException("Events must contain only share outcomes.");
            vm.EventFilter = "逾時"; if (vm.FilteredEvents.Any(e => e.Category != "逾時")) throw new InvalidOperationException("Share outcome filter failed."); vm.EventFilter = "全部";
            Console.WriteLine("PASS: accepted updates within 2 seconds without changing minute hashrate; only success/rejected/timeout in event log.");
            await Execute(vm, vm.StopCommand); await AssertPortClosed(port);
            // Paste the same script; API is inserted automatically and the temporary BAT uses the miner folder.
            vm.Settings.BatchInputMode = vm.Settings.BatchModes[1]; vm.Settings.BatchContent = userShape; vm.Settings.BatchDirectory = directory;
            await Execute(vm, vm.SaveRestartCommand); await WaitForApi(vm);
            if (vm.TotalAccepted != "4" || Directory.GetFiles(directory, "SRBMiner-Monitor-*.bat").Length != 1) throw new InvalidOperationException("Pasted BAT launch failed.");
            var persisted = await File.ReadAllTextAsync(Path.Combine(output, "isolated-settings", "settings.json"));
            if (persisted.Contains("--wallet TEST") || persisted.Contains("\"BatchContent\"")) throw new InvalidOperationException("Pasted BAT must be protected in settings.");
            var restored = await new SettingsStore(Path.Combine(output, "isolated-settings")).LoadAsync();
            if (restored.BatchContent != userShape || !restored.UsePastedBatch) throw new InvalidOperationException("Pasted BAT protection/restore failed.");
            vm.SettingsCommand.Execute(null); await render("pasted-bat-settings"); vm.OverviewCommand.Execute(null);
            await Execute(vm, vm.StopCommand); await AssertPortClosed(port);
            if (Directory.GetFiles(directory, "SRBMiner-Monitor-*.bat").Length != 0) throw new InvalidOperationException("Generated BAT was not removed after stop.");
            vm.Settings.BatchInputMode = vm.Settings.BatchModes[0];
            Console.WriteLine("PASS: pasted BAT -> automatic API -> correct working folder -> protected settings -> stop deletes generated BAT.");
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
            if (vm.Accepted != "4" || vm.TotalAccepted != "4") throw new InvalidOperationException("API plus output counted twice or restart kept old counters.");
            await VerifyScheduledRestart(vm, render);
            await Execute(vm, vm.StopCommand);
            await AssertPortClosed(port);
            Console.WriteLine("PASS: detached BAT restart and stop completed; API port closed.");
            await File.WriteAllTextAsync(Path.Combine(output, "batch-integration.txt"), "PASS: original BAT preserved; monitoring copy adds API; Unicode/space/ampersand/literal-percent/exclamation paths; API -> dashboard; stop kills owned tree; detached START children remain owned; restart returns to API; no mining performed.");
        }
        finally
        {
            if (vm.StopCommand.CanExecute(null)) await Execute(vm, vm.StopCommand);
            try { Directory.Delete(directory, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { StartupDiagnostics.Write("Deferred temporary fixture cleanup: " + e.Message); }
        }
    }
    private static async Task VerifyScheduledRestart(MainViewModel vm, Func<string, Task> render)
    {
        var appPid = Environment.ProcessId;
        string MinerPid() => System.Text.RegularExpressions.Regex.Matches(vm.MinerOutputText, @"FIXTURE_MINER_PID=(\d+)").Last().Groups[1].Value;
        var previousMiner = MinerPid();
        vm.ScheduleEnabled = true; vm.RunValue = "1"; vm.RunUnit = "分鐘"; vm.WaitMinutes = "1"; vm.Repeat = false;
        await Execute(vm, vm.ApplyScheduleCommand);
        Console.WriteLine("Scheduled restart: real 1 minute run + 1 minute wait; application PID=" + appPid + ", miner PID=" + previousMiner);
        var until = DateTimeOffset.Now.AddMinutes(3);
        var sawWaiting = false; var previousSummary = "";
        while (DateTimeOffset.Now < until)
        {
            if (Environment.ProcessId != appPid || !Application.Current.Windows.OfType<MainWindow>().Any(w => w.IsVisible)) throw new InvalidOperationException("Restart closed the application window.");
            if (vm.ScheduleSummary.StartsWith("排程錯誤")) throw new InvalidOperationException(vm.ScheduleSummary);
            if (vm.ScheduleSummary.StartsWith("等待重啟")) sawWaiting = true;
            var phase = vm.ScheduleSummary.Split('·')[0];
            if (phase != previousSummary) { Console.WriteLine("Schedule phase: " + vm.ScheduleSummary); previousSummary = phase; }
            if (sawWaiting && vm.ScheduleSummary == "自動重啟未啟用") break;
            await Task.Delay(200);
        }
        await WaitForApi(vm);
        if (!sawWaiting || vm.ScheduleSummary != "自動重啟未啟用" || MinerPid() == previousMiner || vm.TotalAccepted != "4") throw new InvalidOperationException("Schedule did not restart only the miner: " + vm.ScheduleSummary);
        await render("scheduled-restart-completed");
        Console.WriteLine("PASS: real scheduled stop -> full 1-minute wait -> new miner PID=" + MinerPid() + "; application PID unchanged=" + appPid + "; window remains visible; accepted resets to 4.");
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
