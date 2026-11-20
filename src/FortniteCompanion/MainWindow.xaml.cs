using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FortniteCompanion.ViewModels;

namespace FortniteCompanion;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        // InitializeComponent sırasında ilk Checked olayı sayfalar oluşmadan gelebilir.
        if (OverviewPage == null || MatchesPage == null || FriendsPage == null || ChartsPage == null || SettingsPage == null)
        {
            return;
        }
        OverviewPage.Visibility = NavOverview.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        MatchesPage.Visibility = NavMatches.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        FriendsPage.Visibility = NavFriends.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ChartsPage.Visibility = NavCharts.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (SettingsPage.Visibility == Visibility.Visible)
        {
            SettingsPage.LoadFromSettings();
        }
    }

    public void ShowMatches()
    {
        NavMatches.IsChecked = true;
    }

    // ------------------------------------------------------------------ koyu başlık çubuğu

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    /// <summary>
    /// Windows'un beyaz başlık çubuğu koyu temayla uyumsuz görünür: koyu mod (Windows 10 20H1+) ve
    /// lacivert başlık rengi (Windows 11) istenir. Desteklenmeyen sistemlerde sessizce yok sayılır.
    /// </summary>
    private void ApplyDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            var caption = 0x00330F0A; // COLORREF (0x00BBGGRR): #0A0F33
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            var text = 0x00FFFFFF;
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
        }
        catch (Exception)
        {
            // Eski Windows: varsayılan başlık çubuğu kalır.
        }
    }
}
