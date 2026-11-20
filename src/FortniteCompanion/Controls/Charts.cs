using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using FortniteCompanion.ViewModels;

namespace FortniteCompanion.Controls;

/// <summary>
/// Hafif çubuk grafik: harici kütüphane yok, yalnızca veri değişince yeniden çizilir (GPU/CPU dostu).
/// </summary>
public sealed class BarChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IReadOnlyList<ChartPoint>), typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(BarChart),
        new FrameworkPropertyMetadata(Brushes.Gold, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(BarChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(BarChart),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat), typeof(string), typeof(BarChart),
        new FrameworkPropertyMetadata("0.#", FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChartPoint>? Items
    {
        get => (IReadOnlyList<ChartPoint>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public Brush BarBrush { get => (Brush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public string ValueFormat { get => (string)GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }

    private static readonly Typeface Face = new("Bahnschrift");
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var items = Items;
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 40 || h < 40)
        {
            return;
        }

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // ipucu için isabet alanı
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double bottom = 22, top = 18;
        var plotH = h - bottom - top;

        if (items == null || items.Count == 0)
        {
            var empty = Text("Henüz veri yok", 12, TextBrush, dpi);
            dc.DrawText(empty, new Point((w - empty.Width) / 2, (h - empty.Height) / 2));
            return;
        }

        var max = Math.Max(1, items.Max(i => i.Value));
        var gridPen = new Pen(GridBrush, 1);
        for (var g = 0; g <= 4; g++)
        {
            var y = top + plotH * g / 4.0;
            dc.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }

        var slot = w / items.Count;
        var barW = Math.Max(4, Math.Min(46, slot * 0.62));
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var bh = plotH * (it.Value / max);
            var x = slot * i + (slot - barW) / 2;
            var rect = new Rect(x, top + plotH - bh, barW, Math.Max(0, bh));
            if (bh > 0.5)
            {
                dc.DrawRoundedRectangle(BarBrush, null, rect, 4, 4);
            }

            if (it.Value > 0)
            {
                var vt = Text(it.Value.ToString(ValueFormat, Tr), 11, TextBrush, dpi);
                dc.DrawText(vt, new Point(x + (barW - vt.Width) / 2, rect.Top - vt.Height - 1));
            }

            // Çok sayıda etiket varsa bir atlayarak yaz
            if (items.Count <= 16 || i % 2 == 0)
            {
                var lt = Text(it.Label, 10.5, TextBrush, dpi);
                lt.MaxTextWidth = Math.Max(10, slot);
                lt.Trimming = TextTrimming.CharacterEllipsis;
                lt.MaxLineCount = 1;
                dc.DrawText(lt, new Point(slot * i + Math.Max(0, (slot - lt.Width) / 2), h - bottom + 4));
            }
        }
    }

    internal static FormattedText Text(string s, double size, Brush brush, double dpi) =>
        new(s, Tr, FlowDirection.LeftToRight, Face, size, brush, dpi);
}

/// <summary>Çok serili çizgi grafik (zaman içindeki gelişim).</summary>
public sealed class LineChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IReadOnlyList<string>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChartSeries>? Series
    {
        get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public IReadOnlyList<string>? Labels
    {
        get => (IReadOnlyList<string>?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var series = Series;
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 60 || h < 60)
        {
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (series == null || series.Count == 0 || series.All(s => s.Values.Count == 0))
        {
            var empty = BarChart.Text("Henüz veri yok", 12, TextBrush, dpi);
            dc.DrawText(empty, new Point((w - empty.Width) / 2, (h - empty.Height) / 2));
            return;
        }

        const double left = 48, bottom = 22, top = 26, right = 8;
        var plotW = w - left - right;
        var plotH = h - top - bottom;
        var all = series.SelectMany(s => s.Values).ToList();
        var min = all.Min();
        var max = all.Max();
        if (max - min < 1)
        {
            max = min + 1;
        }
        var pad = (max - min) * 0.08;
        min = Math.Max(0, min - pad);
        max += pad;

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80)), 1);
        for (var g = 0; g <= 4; g++)
        {
            var y = top + plotH * g / 4.0;
            dc.DrawLine(gridPen, new Point(left, y), new Point(w - right, y));
            var v = max - (max - min) * g / 4.0;
            var t = BarChart.Text(v.ToString("N0", Tr), 10, TextBrush, dpi);
            dc.DrawText(t, new Point(left - t.Width - 6, y - t.Height / 2));
        }

        var n = series.Max(s => s.Values.Count);
        double X(int i) => left + (n <= 1 ? plotW / 2 : plotW * i / (n - 1));
        double Y(double v) => top + plotH * (1 - (v - min) / (max - min));

        var legendX = left;
        foreach (var s in series)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Color));
            brush.Freeze();
            var pen = new Pen(brush, 2.2) { LineJoin = PenLineJoin.Round };
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (var i = 0; i < s.Values.Count; i++)
                {
                    var p = new Point(X(i), Y(s.Values[i]));
                    if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);

            // Lejant
            dc.DrawRoundedRectangle(brush, null, new Rect(legendX, 6, 12, 12), 3, 3);
            var lt = BarChart.Text(s.Name, 11, TextBrush, dpi);
            dc.DrawText(lt, new Point(legendX + 16, 5));
            legendX += 16 + lt.Width + 18;
        }

        var labels = Labels;
        if (labels != null && labels.Count > 0)
        {
            var step = Math.Max(1, labels.Count / 8);
            for (var i = 0; i < labels.Count; i += step)
            {
                var t = BarChart.Text(labels[i], 10, TextBrush, dpi);
                dc.DrawText(t, new Point(X(i) - t.Width / 2, h - bottom + 4));
            }
        }
    }
}
