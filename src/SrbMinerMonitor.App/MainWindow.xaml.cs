using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SrbMinerMonitor.App.Services;
using SrbMinerMonitor.App.ViewModels;
using Forms = System.Windows.Forms;

namespace SrbMinerMonitor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon? _tray;
    private bool _allowClose, _notified;
    public MainWindow(SettingsStore? store = null)
    {
        _viewModel = new MainViewModel(store);
        InitializeComponent();
        DataContext = _viewModel;
        SourceInitialized += (_, _) =>
        {
            try { var dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { StartupDiagnostics.Write("Title bar styling unavailable: " + e.Message); }
        };
        ContentRendered += (_, _) => StartupDiagnostics.Write("Main window rendered.");
        try
        {
            _tray = new Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "SRBMiner Monitor", Visible = true,
                ContextMenuStrip = new Forms.ContextMenuStrip()
            };
            _tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
            _tray.ContextMenuStrip.Items.Add("開啟 SRBMiner Monitor", null, (_, _) => Dispatcher.Invoke(Restore));
            _tray.ContextMenuStrip.Items.Add("結束應用程式", null, (_, _) => Dispatcher.Invoke(() =>
            { if (_viewModel.ExitCommand.CanExecute(null)) _viewModel.ExitCommand.Execute(null); }));
        }
        catch (Exception e)
        {
            _tray?.Dispose();
            _tray = null;
            StartupDiagnostics.Write("System tray unavailable: " + e);
            _viewModel.ShowError(new InvalidOperationException("系統匣無法使用，視窗仍可正常操作；關閉視窗會停止並退出。"));
        }
        _viewModel.ExitRequested += () => { _allowClose = true; Close(); };
        Loaded += async (_, _) => { try { await _viewModel.InitializeAsync(); } catch (Exception e) { _viewModel.ShowError(e); } };
        Closed += (_, _) => { if (_tray is { } tray) { tray.Visible = false; tray.Dispose(); } _viewModel.Dispose(); Application.Current.Shutdown(); };
    }
    private void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_tray is null)
        {
            if (_viewModel.ExitCommand.CanExecute(null)) _viewModel.ExitCommand.Execute(null);
            return;
        }
        Hide();
        if (_notified) return;
        _notified = true;
        _tray.ShowBalloonTip(3000, "SRBMiner Monitor", "程式已縮至系統匣，監控與排程繼續執行。若要結束，請選擇「結束應用程式」。", Forms.ToolTipIcon.Info);
    }
}
