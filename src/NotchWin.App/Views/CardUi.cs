using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>Small builders for the translucent "cards" used by the agents, resources and clipboard tabs.</summary>
internal static class CardUi
{
    public static readonly Color Dim = Color.FromArgb(255, 0x8E, 0x8E, 0x93);
    public static readonly Color Faint = Color.FromArgb(255, 0x63, 0x63, 0x66);
    public static readonly Color Track = Color.FromArgb(255, 0x3A, 0x3A, 0x3C);
    public static readonly Color Danger = Color.FromArgb(255, 0xFF, 0x45, 0x3A);

    public static SolidColorBrush Brush(Color color) => new(color);

    public static Border Card(UIElement child, double padding = 12) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(padding),
        Child = child,
    };

    public static TextBlock Text(string text, double size, Color color, bool bold = false, bool trim = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
        if (bold)
        {
            block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }

        if (trim)
        {
            block.TextTrimming = TextTrimming.CharacterEllipsis;
            block.MaxLines = 1;
        }

        return block;
    }

    public static FontIcon Icon(string glyph, double size, Color color) => new()
    {
        Glyph = glyph,
        FontSize = size,
        Foreground = new SolidColorBrush(color),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A rounded bar. <paramref name="fill"/> is 0–1; <paramref name="marker"/> (0–1) draws a thin tick, like the elapsed-time mark on plan limits.</summary>
    public static Grid Bar(double fill, Color color, double? marker = null, double height = 6)
    {
        fill = Math.Clamp(fill, 0, 1);
        var bar = new Grid { Height = height };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fill, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fill, GridUnitType.Star) });
        var track = new Border { Background = new SolidColorBrush(Track), CornerRadius = new CornerRadius(height / 2) };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        if (fill > 0)
        {
            bar.Children.Add(new Border { Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(height / 2) });
        }

        if (marker is { } m)
        {
            m = Math.Clamp(m, 0.02, 0.98);
            var overlay = new Grid { IsHitTestVisible = false };
            overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(m, GridUnitType.Star) });
            overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - m, GridUnitType.Star) });
            var tick = new Border { Width = 2, Height = height + 6, Background = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(1) };
            Grid.SetColumn(tick, 1);
            overlay.Children.Add(tick);
            Grid.SetColumnSpan(overlay, 2);
            bar.Children.Add(overlay);
        }

        return bar;
    }

    public static Grid Columns(params GridLength[] widths)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        foreach (var width in widths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        return grid;
    }

    public static T At<T>(T element, int column, int row = 0, int columnSpan = 1) where T : FrameworkElement
    {
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
        Grid.SetColumnSpan(element, columnSpan);
        return element;
    }
}
