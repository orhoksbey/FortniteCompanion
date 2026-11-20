using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FortniteCompanion.Core.Data;
using FortniteCompanion.ViewModels;

namespace FortniteCompanion.Views;

/// <summary>Değişiklik geçmişi (silinemez günlük) görüntüleyici.</summary>
public sealed class EditLogWindow : Window
{
    public EditLogWindow(Database db)
    {
        Title = "Değişiklik geçmişi";
        Width = 1000;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackground");
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];

        var items = db.GetEditLog(1000).Select(e => new EditLogItem
        {
            When = e.TimestampUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.GetCultureInfo("tr-TR")),
            Entity = e.Entity,
            Key = e.EntityKey,
            Field = e.Field,
            Old = e.OldValue ?? "",
            New = e.NewValue ?? "",
            Reason = e.Reason ?? "",
        }).ToList();

        var grid = new DataGrid { ItemsSource = items, Margin = new Thickness(12) };
        void Col(string header, string path, double width)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path),
                Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
            });
        }
        Col("Zaman", nameof(EditLogItem.When), 140);
        Col("Tür", nameof(EditLogItem.Entity), 80);
        Col("Kayıt", nameof(EditLogItem.Key), 170);
        Col("Alan", nameof(EditLogItem.Field), 110);
        Col("Eski", nameof(EditLogItem.Old), 140);
        Col("Yeni", nameof(EditLogItem.New), 160);
        Col("Neden", nameof(EditLogItem.Reason), 0);

        var hint = new TextBlock
        {
            Text = "Bu günlük silinemez. Geçmiş değer düzenlemeleri, hariç tutmalar, içe aktarmalar ve doğrulamalar burada görünür.",
            Margin = new Thickness(14, 12, 14, 0),
        };
        hint.SetResourceReference(StyleProperty, "HintText");

        var root = new DockPanel();
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);
        root.Children.Add(grid);
        Content = root;
    }
}
