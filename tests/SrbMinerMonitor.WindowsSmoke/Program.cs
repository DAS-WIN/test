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
using System.Text.Json;
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
        if (ConsoleBatchHost.TryHandleArguments(args, out var batchExit)) return batchExit;
        if (ConsoleStopSignal.TryHandleArguments(args, out var helperExit)) return helperExit;
        if (Native.GetConsoleWindow() != IntPtr.Zero) { Console.Error.WriteLine("GUI integration host must not have an attached console."); return 1; }
        Console.WriteLine("PASS: integration host is WinExe with no attached console.");
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/screenshots");
        Directory.CreateDirectory(output);
        StartupDiagnostics.SuppressDialogs = true;
        StartupDiagnostics.Initialize();
        VerifyDefaultRunBindingRejectsReadOnlySource();
        var errors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var app = new SrbMinerMonitor.App.App(createStartupWindow: false);
        app.InitializeComponent();
        // Initialize resources without invoking App.OnStartup, which would open a second window.
        var window = new MainWindow( new SettingsStore(Path.Combine(output, "isolated-settings")));
        app.MainWindow = window;
        var vm = (MainViewModel)window.DataContext;
        var result = 0;
        window.Loaded += async (_, _) =>
        {
            try
            {
                var idlePolls = vm.ApiPollCount;
                await Task.Delay(1200);
                if (vm.ApiPollCount != idlePolls) throw new InvalidOperationException("Idle GUI queried an unstarted miner.");
                if (vm.Settings.RefreshValue != "1" || vm.Settings.RefreshUnit != "分" || !vm.Settings.RefreshUnits.SequenceEqual(new[] { "秒", "分", "時", "天" })) throw new InvalidOperationException("Refresh interval must default to one minute with four units.");
                if (vm.Gpus.Count != 0 || vm.Hashrate != "—") throw new InvalidOperationException("Startup must be empty; no demonstration data or automatic mining.");
                if (vm.Settings.RestartOnUnexpectedExit) throw new InvalidOperationException("Unexpected-exit recovery must default to off.");
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
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: GUI subsystem host without an attached console; actual Stop/Restart buttons retain the same visible GUI; CMD-only recent success and API-only numeric counters; dark theme; no demo or non-BAT launch; accepted above hashrate and no banner; event log contains only success/failure/timeout; minute hashrate independent of live shares; existing and pasted BAT; protected settings; BAT launch, real loopback HTTP polling, stop, restart and detached child ownership using a non-mining fixture; Run.Text OneWay; three pages, schedule, 1440x900/1100x720 rendered; no binding errors.");
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
    private static async Task InvokeMinerButton(MainViewModel vm, string name)
    {
        var window = Application.Current.Windows.OfType<MainWindow>().Single();
        var pid = Environment.ProcessId; var exitRequested = false;
        void OnExit() => exitRequested = true;
        vm.ExitRequested += OnExit;
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var button = Descendants<Button>(window).Single(b => b.Name == name);
            if (!button.Command.CanExecute(null)) throw new InvalidOperationException("Miner action is not available: " + name);
            var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
            var invoke = (System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke);
            invoke.Invoke();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var until = DateTimeOffset.Now.AddSeconds(25);
            while ((vm.IsBusy || button.Command is AsyncCommand { IsExecuting: true }) && DateTimeOffset.Now < until) await Task.Delay(50);
            if (vm.IsBusy || button.Command is AsyncCommand { IsExecuting: true }) throw new TimeoutException("Miner button did not finish: " + name);
            if (exitRequested || Environment.ProcessId != pid || !window.IsVisible || Native.GetConsoleWindow() != IntPtr.Zero) throw new InvalidOperationException("Miner stop/restart closed the GUI or attached it to a console.");
            if (name == "StopMinerButton" && !vm.Status.StartsWith("礦工已停止")) throw new InvalidOperationException("Stop button did not stop only the miner: " + vm.Status);
            if (name == "RestartMinerButton" && !vm.Status.StartsWith("BAT 運行中")) throw new InvalidOperationException("Restart button did not restart the miner: " + vm.Status);
            Console.WriteLine("PASS: " + name + " completed; GUI PID unchanged=" + pid + "; visible, responsive, and never attached to miner console.");
        }
        finally { vm.ExitRequested -= OnExit; }
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
        var sensorFixtureDirectory = Path.GetDirectoryName(executable)!;
        File.Copy(executable, Path.Combine(sensorFixtureDirectory, "nvidia-smi.exe"), true);
        var previousSearchPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", sensorFixtureDirectory + Path.PathSeparator + previousSearchPath);
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
            var outputDeadline = DateTimeOffset.Now.AddSeconds(2);
            while ((vm.TotalAccepted != "4" || vm.Rejected != "1") && DateTimeOffset.Now < outputDeadline) await Task.Delay(20);
            if (vm.TotalAccepted != "4" || vm.Rejected != "1" || vm.Pool != "fixture.example:8048") throw new InvalidOperationException("Output shares or BAT pool missing: " + vm.TotalAccepted + " / " + vm.Pool + " / terminal: " + vm.MinerOutputText);
            vm.SelectedGpu = vm.Gpus[0];
            if (vm.Accepted != "4" || vm.Acceptance != "80.0 %") throw new InvalidOperationException("Selected GPU hides accepted shares: " + vm.Accepted);
            if (!vm.MinerOutputText.Contains("FIXTURE_OUTPUT_REDIRECTED=False") || vm.MinerOutputText.Contains("FIXTURE_CONSOLE_VISIBLE=True") || !vm.MinerOutputText.Contains("FIXTURE_CONSOLE_VISIBLE=False")) throw new InvalidOperationException("Miner must have hidden live console output.");
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
            await InvokeMinerButton(vm, "StopMinerButton"); await AssertPortClosed(port);
            // Paste the same script; API is inserted automatically and the temporary BAT uses the miner folder.
            vm.Settings.BatchInputMode = vm.Settings.BatchModes[1]; vm.Settings.BatchContent = userShape; vm.Settings.BatchDirectory = directory;
            await Execute(vm, vm.SaveRestartCommand); await WaitForApi(vm);
            if (vm.TotalAccepted != "4" || Directory.GetFiles(directory, "SRBMiner-Monitor-*.bat").Length != 1) throw new InvalidOperationException("Pasted BAT launch failed.");
            var persisted = await File.ReadAllTextAsync(Path.Combine(output, "isolated-settings", "settings.json"));
            if (persisted.Contains("--wallet TEST") || persisted.Contains("\"BatchContent\"")) throw new InvalidOperationException("Pasted BAT must be protected in settings.");
            var restored = await new SettingsStore(Path.Combine(output, "isolated-settings")).LoadAsync();
            if (restored.BatchContent != userShape || !restored.UsePastedBatch) throw new InvalidOperationException("Pasted BAT protection/restore failed.");
            vm.SettingsCommand.Execute(null); await render("pasted-bat-settings"); vm.OverviewCommand.Execute(null);
            await InvokeMinerButton(vm, "StopMinerButton"); await AssertPortClosed(port);
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
            await InvokeMinerButton(vm, "RestartMinerButton");
            Console.WriteLine("Restart result: " + vm.Status + " / " + vm.Notice + " / " + vm.ApiStatus);
            await WaitForApi(vm);
            if (!vm.ApiStatus.StartsWith("API 已連線")) throw new InvalidOperationException("BAT restart failed.");
            if (vm.Accepted != "4" || vm.TotalAccepted != "4") throw new InvalidOperationException("API plus output counted twice or restart kept old counters.");
            await VerifyRefreshAndLiveFeed(vm, directory, output, render);
            await VerifyScheduledRestart(vm, render);
            await VerifyUnexpectedExitRecovery(vm, directory, output, render);
            await InvokeMinerButton(vm, "StopMinerButton");
            await AssertPortClosed(port);
            Console.WriteLine("PASS: detached BAT restart and stop completed; API port closed.");
            await File.WriteAllTextAsync(Path.Combine(output, "batch-integration.txt"), "PASS: original BAT preserved; monitoring copy adds API; Unicode/space/ampersand/literal-percent/exclamation paths; API -> dashboard; stop kills owned tree; detached START children remain owned; restart returns to API; no mining performed.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousSearchPath);
            if (vm.StopCommand.CanExecute(null)) { vm.OverviewCommand.Execute(null); await InvokeMinerButton(vm, "StopMinerButton"); }
            try { Directory.Delete(directory, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { StartupDiagnostics.Write("Deferred temporary fixture cleanup: " + e.Message); }
        }
    }
    private static async Task VerifyRefreshAndLiveFeed(MainViewModel vm, string directory, string output, Func<string, Task> render)
    {
        string MinerPid() => System.Text.RegularExpressions.Regex.Matches(vm.MinerOutputText, @"FIXTURE_MINER_PID=(\d+)").Last().Groups[1].Value;
        string Hardware() => string.Join("|", new[] { vm.Hashrate, vm.Power, vm.Efficiency, vm.Temperature }.Concat(vm.Gpus.Select(g => string.Join("/", g.Hashrate, g.Power, g.Efficiency, g.Fan, g.Temperature, g.Core, g.Memory))));
        var miner = MinerPid();
        var previousLogDirectory = vm.MiningLogDirectory;
        await vm.FlushMiningLogsAsync();
        var previousLogCount = ReadMiningRecords(vm).Count;
        vm.Settings.MiningLogDirectory = Path.Combine(output, "custom-share-data");
        vm.Settings.RefreshValue = "1"; vm.Settings.RefreshUnit = "天";
        await Execute(vm, vm.SaveCommand);
        if (vm.Power == "—" || vm.Temperature == "—") throw new InvalidOperationException("Hardware sensor fixture did not bind.");
        var frozen = Hardware(); var historyCount = vm.History.Samples.Count;
        string Averages() => string.Join("|", vm.AcceptedAverage10m, vm.AcceptedAverage30m, vm.AcceptedAverage1h, vm.AcceptedAverageOverall, vm.AcceptedAverageUpdated);
        var frozenAverages = Averages();
        if (vm.MiningLogDirectory != Path.GetFullPath(vm.Settings.MiningLogDirectory)) throw new InvalidOperationException("Custom log directory did not apply live.");
        var successes = vm.Events.Count(e => e.Category == "成功");
        var recentId = vm.RecentShares.First().Id;
        await File.WriteAllTextAsync(Path.Combine(directory, "fixture-api-accepted"), "go");
        var deadline = DateTimeOffset.Now.AddSeconds(3);
        while (vm.TotalAccepted != "5" && DateTimeOffset.Now < deadline) await Task.Delay(20);
        if (vm.TotalAccepted != "5") throw new InvalidOperationException("Accepted numeric counter did not update from API.");
        if (vm.RecentShares.First().Id != recentId || vm.Events.Count(e => e.Category == "成功") != successes) throw new InvalidOperationException("API created a textual success entry; recent shares must contain only CMD output.");
        await Task.Delay(1200);
        if (Hardware() != frozen || vm.History.Samples.Count != historyCount) throw new InvalidOperationException("Hardware changed before configured daily interval.");
        await File.WriteAllTextAsync(Path.Combine(directory, "fixture-flush-accepted"), "go");
        deadline = DateTimeOffset.Now.AddSeconds(3);
        while (!vm.RecentShares.First().Message.Contains("DELAYED_FIXTURE") && DateTimeOffset.Now < deadline) await Task.Delay(20);
        const string expected = "GPU0[t0] share accepted [777ms] [pearlhash][0] DELAYED_FIXTURE";
        if (vm.RecentShares.First().Message != expected || vm.Events.Count(e => e.Category == "成功") != successes + 1 || vm.TotalAccepted != "5" || vm.RecentShares.Any(e => e.Message.Contains("API 已確認"))) throw new InvalidOperationException("Recent shares must preserve the exact CMD message without API text or duplicate counts.");
        Console.WriteLine("PASS: API only updates numeric counters; recent-success panel stays unchanged until CMD output, then shows the exact original line once.");
        await vm.FlushMiningLogsAsync();
        var baselineRecords = ReadMiningRecords(vm);
        if (baselineRecords.Count != 1 || baselineRecords[0].GetProperty("share_text").GetString() != expected)
            throw new InvalidOperationException("API-only increments or hardware updates wrote synthetic log entries.");
        var activeSession = baselineRecords[0].GetProperty("session").GetGuid();
        await File.WriteAllTextAsync(Path.Combine(directory, "fixture-difficulty"), "go");
        deadline = DateTimeOffset.Now.AddSeconds(3);
        while (!vm.MinerOutputText.Contains("[diff: 2000]") && DateTimeOffset.Now < deadline) await Task.Delay(20);
        if (!vm.MinerOutputText.Contains("[diff: 2000]")) throw new InvalidOperationException("Difficulty output was not observed.");
        await File.WriteAllTextAsync(Path.Combine(directory, "fixture-accepted"), "go");
        deadline = DateTimeOffset.Now.AddSeconds(3);
        while (vm.TotalAccepted != "6" && DateTimeOffset.Now < deadline) await Task.Delay(20);
        await vm.FlushMiningLogsAsync();
        var records = ReadMiningRecords(vm).Where(e => e.GetProperty("session").GetGuid() == activeSession).ToArray();
        if (records.Length != 2) throw new InvalidOperationException("Expected exactly one log row per raw accepted share.");
        var lastAccepted = records[^1];
        if (lastAccepted.GetProperty("pool_difficulty").GetDouble() != 2000
            || lastAccepted.GetProperty("pool_difficulty_timestamp").ValueKind != JsonValueKind.String
            || lastAccepted.GetProperty("share_timestamp").ValueKind != JsonValueKind.String
            || lastAccepted.GetProperty("hashrate_hps").GetDouble() <= 0
            || lastAccepted.GetProperty("hashrate_timestamp").GetDateTimeOffset() <= baselineRecords[0].GetProperty("hashrate_timestamp").GetDateTimeOffset())
            throw new InvalidOperationException("Share log is missing the actual timestamp, latest API rate, or preceding pool difficulty.");
        foreach (var row in records)
        {
            if (row.TryGetProperty("accepted_10m_per_minute", out _) || row.TryGetProperty("accepted_30m_per_minute", out _)
                || row.TryGetProperty("accepted_1h_per_minute", out _) || row.TryGetProperty("kind", out _))
                throw new InvalidOperationException("Average or non-share log metadata remains.");
            if (!row.GetProperty("share_text").GetString()!.Contains("share accepted")) throw new InvalidOperationException("Share text missing.");
        }
        if (Hardware() != frozen || Averages() != frozenAverages)
            throw new InvalidOperationException("Live shares refreshed hardware or accepted averages before the configured interval.");
        var oldRows = Directory.GetFiles(previousLogDirectory, "*.log").Sum(p => File.ReadAllLines(p).Length);
        if (oldRows != previousLogCount) throw new InvalidOperationException("New records were still written to the old directory.");
        await render("accepted-averages-and-mining-log");
        await File.WriteAllTextAsync(Path.Combine(directory, "fixture-crt-accepted"), "go");
        deadline = DateTimeOffset.Now.AddSeconds(2);
        while (!vm.RecentShares.Any(e => e.Message.Contains("LIVE_CRT_FIXTURE")) && DateTimeOffset.Now < deadline) await Task.Delay(20);
        if (!vm.RecentShares.Any(e => e.Message.Contains("LIVE_CRT_FIXTURE")))
            throw new InvalidOperationException("Native CRT stdout was not delivered within 2 seconds without fflush. Terminal: " + vm.MinerOutputText);
        if (!vm.MinerOutputText.Contains("NATIVE_CRT_TTY=64") && !vm.MinerOutputText.Contains("NATIVE_CRT_TTY=1"))
            throw new InvalidOperationException("Native child CRT stdout is not a terminal: " + vm.MinerOutputText);
        Console.WriteLine("PASS: native CRT printf accepted reaches recent shares within 2 seconds through the pseudoconsole without explicit flush; GUI has no console.");
        Console.WriteLine("PASS: raw accepted-only log rows; original text and share timestamp, latest API hashrate and preceding pool difficulty; no average records or API duplicates; live custom directory switching; 10m/30m/1h/overall averages remain frozen with hardware.");

        vm.Settings.RefreshValue = "2"; vm.Settings.RefreshUnit = "秒";
        await Execute(vm, vm.SaveCommand);
        var before = Hardware(); var power = vm.Power; var rate = vm.Hashrate;
        deadline = DateTimeOffset.Now.AddSeconds(7);
        while ((vm.Power == power || vm.Hashrate == rate) && DateTimeOffset.Now < deadline) await Task.Delay(100);
        if (vm.Power == power || vm.Hashrate == rate || MinerPid() != miner) throw new InvalidOperationException("Custom seconds interval did not refresh hardware without restarting the miner.");
        if (Averages() == frozenAverages || !vm.AcceptedAverageOverall.Contains("新增 7 筆")
            || vm.AcceptedAverageUpdated != vm.HashrateUpdate)
            throw new InvalidOperationException("Accepted averages must refresh with the same hardware sample timestamp.");
        var restored = await new SettingsStore(Path.Combine(output, "isolated-settings")).LoadAsync();
        if (restored.MiningLogDirectory != vm.Settings.MiningLogDirectory) throw new InvalidOperationException("Custom directory was not persisted.");
        if (restored.MetricRefresh != new MetricRefreshOptions(2, RefreshUnit.Seconds)) throw new InvalidOperationException("Custom interval did not persist.");
        vm.SettingsCommand.Execute(null); await render("custom-refresh-settings"); vm.OverviewCommand.Execute(null); await render("live-share-fixed-hardware-interval");
        vm.Settings.RefreshValue = "0"; await Execute(vm, vm.SaveCommand);
        if (!vm.Notice.Contains("正整數")) throw new InvalidOperationException("Invalid interval was silently accepted.");
        vm.Settings.RefreshValue = "1"; vm.Settings.RefreshUnit = "分"; await Execute(vm, vm.SaveCommand);
        Console.WriteLine("PASS: seconds/minutes/hours/days selector; custom 2-second hardware updates, persisted setting and invalid-zero feedback; saving does not restart miner.");
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
            if (Native.GetConsoleWindow() != IntPtr.Zero || Environment.ProcessId != appPid || !Application.Current.Windows.OfType<MainWindow>().Any(w => w.IsVisible)) throw new InvalidOperationException("Restart closed the application window.");
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
    private static async Task VerifyUnexpectedExitRecovery(MainViewModel vm, string directory, string output, Func<string, Task> render)
    {
        var appPid = Environment.ProcessId;
        var initialCrashes = vm.UnexpectedExitCount;
        var window = Application.Current.Windows.OfType<MainWindow>().Single();
        string MinerPid() => System.Text.RegularExpressions.Regex.Matches(vm.MinerOutputText, @"FIXTURE_MINER_PID=(\d+)").Last().Groups[1].Value;
        async Task Until(Func<bool> condition, int seconds)
        {
            var deadline = DateTimeOffset.Now.AddSeconds(seconds);
            while (!condition() && DateTimeOffset.Now < deadline)
            {
                if (!window.IsVisible || Environment.ProcessId != appPid || Native.GetConsoleWindow() != IntPtr.Zero) throw new InvalidOperationException("Crash recovery closed or attached the GUI.");
                await Task.Delay(50);
            }
            if (!condition()) throw new TimeoutException("Crash recovery did not advance: " + vm.ScheduleSummary + " / " + vm.Notice);
        }
        async Task CrashFixture()
        {
            // This PID comes only from our owned, non-mining fixture's output.
            using var fixture = Process.GetProcessById(int.Parse(MinerPid()));
            fixture.Kill(); await fixture.WaitForExitAsync();
        }
        // Test the original user's BAT shape with trailing PAUSE, not just detached START.
        var monitoring = Path.Combine(directory, "original.monitor.bat");
        vm.Settings.BatchFile = monitoring; vm.Settings.RestartOnUnexpectedExit = true;
        await Execute(vm, vm.SaveRestartCommand); await WaitForApi(vm);
        vm.SettingsCommand.Execute(null); await render("unexpected-exit-settings");
        if (Descendants<CheckBox>(window).Single(c => c.Name == "UnexpectedExitRestartOption").IsChecked != true) throw new InvalidOperationException("Recovery checkbox did not bind.");
        vm.OverviewCommand.Execute(null);
        if (!(await new SettingsStore(Path.Combine(output, "isolated-settings")).LoadAsync()).RestartOnUnexpectedExit) throw new InvalidOperationException("Recovery option did not persist.");
        var previous = MinerPid();
        var watch = Stopwatch.StartNew(); await CrashFixture();
        await Until(() => vm.ScheduleSummary.StartsWith("異常退出 · 等待重啟"), 8);
        if (vm.UnexpectedExitCount != initialCrashes + 1) throw new InvalidOperationException("Crash counter did not increment exactly once.");
        var pausedPolls = vm.ApiPollCount; var pausedUpdate = vm.LastUpdate; var pausedAverage = vm.AcceptedAverageOverall;
        await Task.Delay(1500);
        if (vm.ApiPollCount != pausedPolls || vm.LastUpdate != pausedUpdate || vm.AcceptedAverageOverall != pausedAverage)
            throw new InvalidOperationException("Nonexistent miner was still being monitored during recovery wait.");
        await render("unexpected-exit-countdown");
        await Until(() => MinerPid() != previous && vm.Status.StartsWith("BAT 運行中"), 18);
        await WaitForApi(vm);
        if (watch.Elapsed < TimeSpan.FromSeconds(10)) throw new InvalidOperationException("Standalone recovery skipped the 10-second wait.");
        Console.WriteLine("PASS: recovery enabled without schedule; killed owned fixture in BAT with PAUSE -> 10-second countdown -> new miner PID; GUI PID unchanged=" + appPid + "; setting persisted.");
        // Manual stop while recovery is waiting must cancel any pending relaunch.
        await CrashFixture(); await Until(() => vm.ScheduleSummary.StartsWith("異常退出 · 等待重啟"), 8);
        await InvokeMinerButton(vm, "StopMinerButton"); previous = MinerPid();
        pausedPolls = vm.ApiPollCount;
        await Task.Delay(TimeSpan.FromSeconds(12));
        if (vm.ApiPollCount != pausedPolls || vm.UnexpectedExitCount != initialCrashes + 2)
            throw new InvalidOperationException("Manual stop kept monitoring or incorrectly changed the crash count.");
        if (MinerPid() != previous || !vm.Status.StartsWith("礦工已停止")) throw new InvalidOperationException("Manual stop did not cancel recovery.");
        Console.WriteLine("PASS: manual stop during crash countdown cancels pending restart even with recovery setting enabled.");
        await Execute(vm, vm.StartCommand); await WaitForApi(vm);
        vm.Settings.RestartOnUnexpectedExit = false; await Execute(vm, vm.SaveCommand);
        vm.ScheduleEnabled = true; vm.RunValue = "10"; vm.RunUnit = "分鐘"; vm.WaitMinutes = "1"; vm.Repeat = false;
        await Execute(vm, vm.ApplyScheduleCommand); previous = MinerPid(); await CrashFixture();
        await Until(() => vm.ScheduleSummary.StartsWith("等待重啟"), 8);
        watch.Restart(); await render("scheduled-unexpected-exit-countdown");
        await Task.Delay(TimeSpan.FromSeconds(12));
        if (MinerPid() != previous || !vm.ScheduleSummary.StartsWith("等待重啟")) throw new InvalidOperationException("Scheduled recovery used the independent 10-second timer instead of configured wait.");
        await Until(() => MinerPid() != previous && vm.Status.StartsWith("BAT 運行中"), 65); await WaitForApi(vm);
        if (watch.Elapsed < TimeSpan.FromSeconds(59) || vm.ScheduleSummary != "自動重啟未啟用") throw new InvalidOperationException("Scheduled crash recovery skipped full wait or did not complete one-shot.");
        if (vm.UnexpectedExitCount != initialCrashes + 3) throw new InvalidOperationException("Scheduled recovery did not retain the crash counter.");
        await render("scheduled-unexpected-exit-recovered");
        Console.WriteLine("PASS: unexpected exits counted once each, manual/scheduled stops excluded; no API or average updates while stopped/recovering; monitoring resumes after restart.");
        Console.WriteLine("PASS: recovery checkbox OFF + timed schedule ON; crash interrupts 10-minute run -> configured full 1-minute wait -> exactly one new miner, one-shot completes; GUI PID unchanged=" + appPid + ".");
    }
    private static List<JsonElement> ReadMiningRecords(MainViewModel vm)
    {
        var rows = new List<JsonElement>();
        foreach (var path in Directory.Exists(vm.MiningLogDirectory) ? Directory.GetFiles(vm.MiningLogDirectory, "*.log") : [])
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                using var document = JsonDocument.Parse(line); rows.Add(document.RootElement.Clone());
            }
        }
        return rows.OrderBy(e => e.GetProperty("timestamp").GetDateTimeOffset()).ToList();
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
        if (!vm.ApiStatus.StartsWith("API 已連線") || !vm.Status.StartsWith("BAT 運行中")) throw new InvalidOperationException("API not ready: " + vm.Status + " / " + vm.Notice + " / " + vm.ApiStatus + " / terminal: " + vm.MinerOutputText);
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
    private static class Native
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    }
}
