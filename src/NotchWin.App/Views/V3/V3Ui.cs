using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.Core.Notch;
using NotchWin.Core.Notch.V3;
using Windows.UI;

namespace NotchWin.App.Views.V3;

public enum ActionStyle { Secondary, Primary, Danger, Ghost }

public enum BadgeTone { Neutral, Accent, Success, Warning, Danger }

/// <summary>
/// Hatsu Notch V3 — componentes compartidos. Extienden <see cref="CardUi"/> y <see cref="Pal"/> (no son una segunda arquitectura): el tema claro/oscuro sigue saliendo de Pal y los
/// colores de V3Palette (Core, con contraste comprobado). Cada componente se reconstruye con el tema vigente cuando la vista se vuelve a pintar.
/// </summary>
internal static class V3Ui
{
    private static V3Colors K => V3Palette.For(Pal.Light);
    private static Color Col(Argb a) => Color.FromArgb(a.A, a.R, a.G, a.B);
    private static SolidColorBrush Br(Argb a) => new(Col(a));

    public static Color Accent => Col(K.Accent);
    public static Color AccentText => Col(K.AccentText);
    public static Color Danger => Col(K.Danger);
    public static Color Success => Col(K.Success);
    public static Color Warning => Col(K.Warning);
    public static Color Dim => CardUi.Dim;

    // ───────────── superficies
    /// <summary>Tarjeta translúcida (NotchSurface de las vistas): fondo suave, borde fino y esquina del sistema.</summary>
    public static Border Surface(UIElement child, double padding = V3Palette.Pad, bool interactive = false)
    {
        var b = new Border { Background = Br(K.Surface), BorderBrush = Br(K.SurfaceBorder), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(V3Palette.RadiusCard), Padding = new Thickness(padding), Child = child };
        if (interactive)
        {
            b.PointerEntered += (_, _) => b.Background = Br(K.SurfaceHover);
            b.PointerExited += (_, _) => b.Background = Br(K.Surface);
        }

        return b;
    }

    /// <summary>Cabecera de módulo: icono en una ficha de acento, título, subtítulo opcional y un control a la derecha.</summary>
    public static Grid ModuleHeader(string glyph, string title, string? subtitle = null, UIElement? trailing = null)
    {
        var g = new Grid { ColumnSpacing = 10 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chip = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x33, K.Accent.R, K.Accent.G, K.Accent.B)), Child = CardUi.Icon(glyph, 15, AccentText) };
        chip.Child.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        g.Children.Add(chip);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(CardUi.Text(title, 14, Pal.Fg, bold: true, trim: true));
        if (!string.IsNullOrEmpty(subtitle)) texts.Children.Add(CardUi.Text(subtitle, 11.5, Dim, trim: true));
        Grid.SetColumn(texts, 1); g.Children.Add(texts);
        if (trailing is FrameworkElement t) { t.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(t, 2); g.Children.Add(t); }
        return g;
    }

    // ───────────── botones
    /// <summary>Botón redondo de icono con los cuatro estados (reposo, encima, pulsado, deshabilitado) y foco visible para teclado. El tooltip dice lo que hace.</summary>
    public static Button ActionButton(string glyph, string tooltip, Action onClick, ActionStyle style = ActionStyle.Secondary, bool enabled = true, double size = 36)
    {
        var b = new Button { Width = size, Height = size, Padding = new Thickness(0), CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(0), IsEnabled = enabled, UseSystemFocusVisuals = true, Content = CardUi.Icon(glyph, size * 0.42, Colors.White) };
        Style(b, style, Colors.White);
        ToolTipService.SetToolTip(b, tooltip); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, tooltip);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Botón de texto (pastilla). <see cref="ActionStyle.Primary"/> = relleno de acento.</summary>
    public static Button TextButton(string text, Action onClick, ActionStyle style = ActionStyle.Secondary, bool enabled = true)
    {
        var b = new Button { Padding = new Thickness(14, 6, 14, 7), CornerRadius = new CornerRadius(V3Palette.RadiusControl), BorderThickness = new Thickness(0), IsEnabled = enabled, UseSystemFocusVisuals = true, Content = new TextBlock { Text = text, FontSize = 12.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold } };
        Style(b, style, Colors.White);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, text);
        b.Click += (_, _) => onClick();
        return b;
    }

    private static void Style(Button b, ActionStyle style, Color onFill)
    {
        Color rest, over, down, fg;
        switch (style)
        {
            case ActionStyle.Primary: rest = Col(K.Accent); over = Lighten(rest, 0.12); down = Darken(rest, 0.12); fg = Col(K.OnAccent); break;
            case ActionStyle.Danger: rest = Color.FromArgb(0x33, K.Danger.R, K.Danger.G, K.Danger.B); over = Color.FromArgb(0x55, K.Danger.R, K.Danger.G, K.Danger.B); down = Color.FromArgb(0x77, K.Danger.R, K.Danger.G, K.Danger.B); fg = Col(K.Danger); break;
            case ActionStyle.Ghost: rest = Colors.Transparent; over = Col(K.SurfaceHover); down = Col(K.SurfaceBorder); fg = Pal.Fg; break;
            default: rest = Col(K.Surface); over = Col(K.SurfaceHover); down = Col(K.SurfaceBorder); fg = Pal.Fg; break;
        }

        b.Resources["ButtonBackground"] = new SolidColorBrush(rest);
        b.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(over);
        b.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(down);
        b.Resources["ButtonBackgroundDisabled"] = new SolidColorBrush(Color.FromArgb(0x10, fg.R, fg.G, fg.B));
        b.Resources["ButtonForeground"] = new SolidColorBrush(fg);
        b.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(fg);
        b.Resources["ButtonForegroundPressed"] = new SolidColorBrush(fg);
        b.Resources["ButtonForegroundDisabled"] = new SolidColorBrush(Color.FromArgb(0x66, fg.R, fg.G, fg.B));
        b.Resources["ButtonBorderBrushPointerOver"] = new SolidColorBrush(Colors.Transparent);
        b.Resources["ButtonBorderBrushPressed"] = new SolidColorBrush(Colors.Transparent);
        b.Background = new SolidColorBrush(rest); b.Foreground = new SolidColorBrush(fg);
        if (b.Content is FontIcon icon) icon.Foreground = new SolidColorBrush(fg);
        _ = onFill;
    }

    private static Color Lighten(Color c, double f) => Color.FromArgb(c.A, (byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
    private static Color Darken(Color c, double f) => Color.FromArgb(c.A, (byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));

    // ───────────── controles
    /// <summary>Segmentos tipo «Cola · Letras · Info»; el elegido va relleno de acento. <paramref name="onSelect"/> recibe el índice.</summary>
    public static Border SegmentedControl(IReadOnlyList<string> items, int selected, Action<int> onSelect)
    {
        var row = new Grid { ColumnSpacing = 2 };
        for (int i = 0; i < items.Count; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int index = i; bool on = i == selected;
            var seg = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 5, 10, 6), CornerRadius = new CornerRadius(V3Palette.RadiusControl - 2), BorderThickness = new Thickness(0), UseSystemFocusVisuals = true, Content = new TextBlock { Text = items[i], FontSize = 12, FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis } };
            Style(seg, on ? ActionStyle.Primary : ActionStyle.Ghost, Colors.White);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(seg, items[i]);
            seg.Click += (_, _) => onSelect(index);
            Grid.SetColumn(seg, i); row.Children.Add(seg);
        }

        return new Border { Background = Br(K.Surface), BorderBrush = Br(K.SurfaceBorder), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(V3Palette.RadiusControl), Padding = new Thickness(2), Child = row };
    }

    /// <summary>Estado con texto Y punto de color (nunca solo color).</summary>
    public static Border StatusBadge(string text, BadgeTone tone)
    {
        var c = tone switch { BadgeTone.Accent => AccentText, BadgeTone.Success => Success, BadgeTone.Warning => Warning, BadgeTone.Danger => Danger, _ => Dim };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        row.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(CardUi.Text(text, 11, c, bold: true));
        return new Border { Background = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B)), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 2, 8, 3), Child = row };
    }

    /// <summary>Barra de progreso: determinada si <paramref name="fraction"/> tiene valor, indeterminada (animación del sistema, solo mientras sea visible) si es null.</summary>
    public static ProgressBar Progress(double? fraction, double height = 4)
    {
        var p = new ProgressBar { Minimum = 0, Maximum = 1, Height = height, MinHeight = height, CornerRadius = new CornerRadius(height / 2), Foreground = new SolidColorBrush(Col(K.GradientFrom)), Background = Br(K.Track) };
        if (fraction is { } f) p.Value = Math.Clamp(f, 0, 1); else p.IsIndeterminate = true;
        return p;
    }

    /// <summary>Estado vacío diseñado: icono, mensaje y, si hay algo útil que hacer, una acción real.</summary>
    public static StackPanel EmptyState(string glyph, string title, string? hint = null, (string Label, Action Act)? action = null)
    {
        var s = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 10) };
        s.Children.Add(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(0x26, K.Accent.R, K.Accent.G, K.Accent.B)), HorizontalAlignment = HorizontalAlignment.Center, Child = CardUi.Icon(glyph, 20, AccentText) });
        s.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Pal.Fg), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center });
        if (!string.IsNullOrEmpty(hint)) s.Children.Add(new TextBlock { Text = hint, FontSize = 11.5, Foreground = new SolidColorBrush(Dim), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Center });
        if (action is { } a) { var b = TextButton(a.Label, a.Act, ActionStyle.Primary); b.HorizontalAlignment = HorizontalAlignment.Center; b.Margin = new Thickness(0, 4, 0, 0); s.Children.Add(b); }
        return s;
    }

    /// <summary>Ficha cuadrada con el icono del tipo de archivo (descargas, portapapeles).</summary>
    public static Border Tile(string glyph, double size = 36, Color? tint = null)
    {
        var c = tint ?? AccentText;
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(V3Palette.RadiusThumb - 2), Background = new SolidColorBrush(Color.FromArgb(0x2B, c.R, c.G, c.B)), Child = CardUi.Icon(glyph, size * 0.45, c) };
    }

    /// <summary>Icono de Segoe Fluent por tipo de archivo.</summary>
    public static string FileGlyph(FileKind k) => k switch
    {
        FileKind.Installer => "", FileKind.Archive => "", FileKind.Document => "", FileKind.Image => "", FileKind.Video => "", FileKind.Audio => "", FileKind.Code => "", _ => "",
    };

    public static string ClipGlyph(ClipKind k) => k switch { ClipKind.Link => "", ClipKind.Code => "", ClipKind.Path => "", ClipKind.Image => "", _ => "" };

    /// <summary>Anillo de progreso con el tiempo (u otro texto) al centro. <paramref name="fraction"/> 0–1 es lo que QUEDA o lo que lleva, según quien lo use.</summary>
    public static Grid Ring(double fraction, double size, string centerText, string? caption = null)
    {
        var g = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        g.Children.Add(new ProgressRing { IsIndeterminate = false, Minimum = 0, Maximum = 100, Value = Math.Clamp(fraction, 0, 1) * 100, Width = size, Height = size, Foreground = new SolidColorBrush(Col(K.GradientFrom)), Background = Br(K.Track) });
        var c = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        c.Children.Add(new TextBlock { Text = centerText, FontSize = size * 0.22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Pal.Fg), HorizontalAlignment = HorizontalAlignment.Center });
        if (!string.IsNullOrEmpty(caption)) c.Children.Add(new TextBlock { Text = caption, FontSize = 11, Foreground = new SolidColorBrush(Dim), HorizontalAlignment = HorizontalAlignment.Center });
        g.Children.Add(c);
        return g;
    }

    /// <summary>Acceso rápido: ficha con icono y nombre debajo. Accesible por teclado y con tooltip.</summary>
    public static Button QuickTile(string glyph, string label, Action onClick)
    {
        var s = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        s.Children.Add(CardUi.Icon(glyph, 18, Pal.Fg));
        s.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(Pal.Fg), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        var b = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Padding = new Thickness(6, 10, 6, 9), CornerRadius = new CornerRadius(V3Palette.RadiusControl), BorderThickness = new Thickness(0), UseSystemFocusVisuals = true, Content = s };
        Style(b, ActionStyle.Secondary, Colors.White);
        ToolTipService.SetToolTip(b, label); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, label);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>mm:ss (o h:mm:ss) para tiempos de reproducción y de temporizador.</summary>
    public static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}
