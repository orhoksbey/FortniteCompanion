using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FortniteCompanion.Views;

/// <summary>Kodla oluşturulan basit form penceresi (metin, sayı, tarih, seçim alanları).</summary>
public sealed class FormDialog : Window
{
    private readonly StackPanel _fields = new();
    private readonly Dictionary<string, FrameworkElement> _inputs = new();
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private Func<FormDialog, string?>? _validate;

    public FormDialog(string title, string? description = null)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackground");
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];

        var root = new StackPanel { Margin = new Thickness(20) };
        var header = new TextBlock { Text = title };
        header.SetResourceReference(StyleProperty, "SectionText");
        root.Children.Add(header);
        if (!string.IsNullOrEmpty(description))
        {
            var d = new TextBlock { Text = description, Margin = new Thickness(0, 0, 0, 10) };
            d.SetResourceReference(StyleProperty, "HintText");
            root.Children.Add(d);
        }
        root.Children.Add(_fields);
        root.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Kaydet", IsDefault = true, MinWidth = 90 };
        ok.SetResourceReference(StyleProperty, "AccentButton");
        ok.Click += (_, _) =>
        {
            var problem = _validate?.Invoke(this);
            if (problem != null)
            {
                _error.Text = problem;
                return;
            }
            DialogResult = true;
        };
        var cancel = new Button { Content = "Vazgeç", IsCancel = true, MinWidth = 90 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;
    }

    public FormDialog Validate(Func<FormDialog, string?> validate)
    {
        _validate = validate;
        return this;
    }

    private void AddLabel(string label)
    {
        var l = new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) };
        l.SetResourceReference(StyleProperty, "LabelText");
        _fields.Children.Add(l);
    }

    public FormDialog Text(string key, string label, string value = "")
    {
        AddLabel(label);
        var box = new TextBox { Text = value };
        _fields.Children.Add(box);
        _inputs[key] = box;
        return this;
    }

    public FormDialog Number(string key, string label, int? value) => Text(key, label, value?.ToString(CultureInfo.InvariantCulture) ?? "");

    public FormDialog Date(string key, string label, DateTime? local)
    {
        AddLabel(label);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var picker = new DatePicker { SelectedDate = local?.Date, Width = 170, Margin = new Thickness(0, 0, 8, 8) };
        var time = new TextBox { Text = (local ?? DateTime.Now).ToString("HH:mm", CultureInfo.InvariantCulture), Width = 70 };
        panel.Children.Add(picker);
        panel.Children.Add(time);
        _fields.Children.Add(panel);
        _inputs[key] = picker;
        _inputs[key + ".time"] = time;
        return this;
    }

    public FormDialog Choice(string key, string label, IEnumerable<string> options, string? selected)
    {
        AddLabel(label);
        var list = options.ToList();
        var combo = new ComboBox { ItemsSource = list, SelectedItem = selected ?? list.FirstOrDefault(), Width = 250, HorizontalAlignment = HorizontalAlignment.Left };
        _fields.Children.Add(combo);
        _inputs[key] = combo;
        return this;
    }

    public string GetText(string key) =>
        _inputs.TryGetValue(key, out var e) && e is TextBox t ? t.Text.Trim() : "";

    public int? GetInt(string key) =>
        int.TryParse(GetText(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    public string? GetChoice(string key) =>
        _inputs.TryGetValue(key, out var e) && e is ComboBox c ? c.SelectedItem as string : null;

    /// <summary>Seçilen yerel tarih + saat (SS:dd). Geçersizse null.</summary>
    public DateTime? GetLocalDateTime(string key)
    {
        if (!_inputs.TryGetValue(key, out var e) || e is not DatePicker p || p.SelectedDate is not DateTime d)
        {
            return null;
        }
        var t = GetText(key + ".time");
        if (TimeSpan.TryParseExact(t, @"hh\:mm", CultureInfo.InvariantCulture, out var ts))
        {
            return d.Date + ts;
        }
        return d.Date;
    }
}
