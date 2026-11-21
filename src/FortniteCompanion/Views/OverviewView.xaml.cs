using System.Windows;
using System.Windows.Controls;

namespace FortniteCompanion.Views;

public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
    }

    private void OnFixTotal(object sender, RoutedEventArgs e) => ProfileDialogs.FixTotal(Window.GetWindow(this));
}
