using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SrbMinerMonitor.App.ViewModels;

namespace SrbMinerMonitor.App.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _settings;
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_settings is not null) _settings.PropertyChanged -= SettingsChanged;
            _settings = (e.NewValue as MainViewModel)?.Settings;
            if (_settings is not null) { _settings.PropertyChanged += SettingsChanged; PoolPassword.Password = _settings.Password; }
        };
    }
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(SettingsViewModel.Password) && PoolPassword.Password != _settings?.Password) PoolPassword.Password = _settings?.Password ?? ""; }
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    { if (_settings is not null) _settings.Password = PoolPassword.Password; }
}
