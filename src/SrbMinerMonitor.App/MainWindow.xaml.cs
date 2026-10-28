using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SrbMinerMonitor.App.ViewModels;
using Forms = System.Windows.Forms;

namespace SrbMinerMonitor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly Forms.NotifyIcon _tray;
    private bool _allowClose, _notified;
    public MainWindow(bool demo)
    {
        InitializeComponent();
        DataContext = _viewModel;
        SourceInitialized += (_, _) => { var dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); };
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "SRBMiner Monitor", Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
        _tray.ContextMenuStrip.Items.Add("開啟 SRBMiner Monitor", null, (_, _) => Dispatcher.Invoke(Restore));
        _tray.ContextMenuStrip.Items.Add("停止並退出", null, (_, _) => Dispatcher.Invoke(() =>
        { if (_viewModel.ExitCommand.CanExecute(null)) _viewModel.ExitCommand.Execute(null); }));
        _viewModel.ExitRequested += () => { _allowClose = true; Close(); };
        Loaded += async (_, _) => { try { await _viewModel.InitializeAsync(demo); } catch (Exception e) { _viewModel.ShowError(e); } };
        Closed += (_, _) => { _tray.Visible = false; _tray.Dispose(); _viewModel.Dispose(); Application.Current.Shutdown(); };
    }
    private void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
        if (_notified) return;
        _notified = true;
        _tray.ShowBalloonTip(3000, "SRBMiner Monitor", "程式已縮至系統匣，監控與排程繼續執行。若要結束，請選擇「停止並退出」。", Forms.ToolTipIcon.Info);
    }
}
