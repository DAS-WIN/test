using System.Windows.Controls;
namespace SrbMinerMonitor.App.Views;
public partial class OverviewView : UserControl
{
    public OverviewView() => InitializeComponent();
    private void OnMinerOutputChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box && !box.IsKeyboardFocusWithin) box.ScrollToEnd();
    }
}
