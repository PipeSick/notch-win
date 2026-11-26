using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.App.Views.V3;
using NotchWin.Core.Downloads;
using NotchWin.Core.Music;
using NotchWin.Core.Notch.V3;
using NotchWin.Core.Settings;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>
/// Inicio V3 (aprobado): minimalista y CONTEXTUAL. Una actividad principal elegida por <see cref="HomeFocus"/> (música sonando → temporizador → descarga → …), hasta tres secundarias en
/// una fila y los accesos rápidos. Nunca un tablero con todo. Cada pieza se alimenta del estado real; nada es decorativo.
/// </summary>
public sealed partial class NotchWindow
{
    private string _homeSignature = "";

    private void OpenSection(string id)
    {
        _active = id; _customizing = false;
        RebuildTabs(); ShowActivePane(); RelayoutIfExpanded();
    }

    private bool SectionOn(string id) => EnabledSections().Any(s => s.Id == id);

    private IReadOnlyList<DownloadView.Row> DownloadRows() => DownloadView.Rows(_viewModel.Downloads, _viewModel.Managed?.Items ?? []);

    private HomeContext BuildHomeContext(IReadOnlyList<DownloadView.Row> rows)
    {
        var phase = TimerView.Phase(TimerStates.Of(_viewModel.Timer));
        return new HomeContext(_info is not null, _info?.IsPlaying == true, phase == TimerPhase.Running, phase is TimerPhase.Paused or TimerPhase.Finished, rows.Count(r => r.IsActive), _viewModel.Inbox.NewCount(_viewModel.Notifications.Items));
    }

    /// <summary>Se reconstruye solo si cambió algo que se ve (la posición de la canción cuenta cada 3 s, las descargas cada 512 KB), para no parpadear ni perder un clic a medias.</summary>
    private void RebuildHome(bool force = false)
    {
        var rows = DownloadRows(); var ctx = BuildHomeContext(rows); var t = _viewModel.Timer;
        var pos = _info is { } i0 ? i0.Position + (i0.IsPlaying ? DateTime.UtcNow - _infoAtUtc : TimeSpan.Zero) : TimeSpan.Zero;
        var sig = string.Join("|", _info?.Title, _info?.Artist, _info?.IsPlaying, (int)(pos.TotalSeconds / 3), _info?.Extras?.Shuffle, _info?.Extras?.Repeat, TimerView.Phase(TimerStates.Of(t)), (int)t.Remaining.TotalSeconds,
            string.Join(",", rows.Where(r => r.IsActive).Select(r => r.Key + r.State + r.ReceivedBytes / 524288)), ctx.Notifications, string.Join(",", _viewModel.QuickAvailable), string.Join(",", EnabledSections().Select(s => s.Id)), string.Join(",", _viewModel.Peripherals.Select(d => d.Name + d.Percent + d.Charging)), string.Join(",", (_viewModel.Notes?.Pinned() ?? []).Select(n => n.Id + n.UpdatedUtc.Ticks + n.Color)), Pal.Light);
        if (!force && sig == _homeSignature) return;
        _homeSignature = sig;

        HomeGrid.Children.Clear(); HomeGrid.RowDefinitions.Clear(); HomeGrid.ColumnDefinitions.Clear();
        var stack = new StackPanel { Spacing = 10 };
        var primary = HomeFocus.Primary(ctx);
        switch (primary)
        {
            case HomePrimary.Music: stack.Children.Add(HomeMusicCard()); break;
            case HomePrimary.Timer: stack.Children.Add(HomeTimerCard()); break;
            case HomePrimary.Download: stack.Children.Add(HomeDownloadCard(rows.First(r => r.IsActive))); break;
            default: stack.Children.Add(V3Ui.EmptyState("", "Nada en marcha", "Cuando suene música, haya una descarga o un temporizador, aparece aquí.")); break;
        }

        // Con música o una descarga como principal el temporizador no se queda en un chip: sigue visible y con sus botones (pausar / reiniciar / cancelar).
        bool timerCard = primary != HomePrimary.Timer && SectionOn(NotchSections.Timer) && TimerView.Phase(TimerStates.Of(_viewModel.Timer)) != TimerPhase.Ready;
        if (timerCard) stack.Children.Add(HomeTimerCompact());
        if (HomeSecondaryRow(ctx, rows) is { } sec) stack.Children.Add(sec);
        if (HomeNotesCard() is { } notes) stack.Children.Add(notes);
        if (HomePeripheralsCard() is { } periph) stack.Children.Add(periph);
        if (HomeQuickRow() is { } quick) stack.Children.Add(quick);
        HomeGrid.Children.Add(stack);
        HomeEmpty.Visibility = Visibility.Collapsed;
    }

    // ───────────── principal: música
    private UIElement HomeMusicCard()
    {
        var info = _info!; var x = info.Extras; var caps = x?.Capabilities ?? MediaCapabilities.None;
        var body = new StackPanel { Spacing = 12 };

        var head = new Grid { ColumnSpacing = 14 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(new Border { Width = 92, Height = 92, CornerRadius = new CornerRadius(16), Background = MiniArt.Background });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
        texts.Children.Add(CardUi.Text(info.Title, 18, Pal.Fg, bold: true, trim: true));
        texts.Children.Add(CardUi.Text(string.IsNullOrWhiteSpace(info.Album) ? info.Artist : info.Artist + " · " + info.Album, 12.5, CardUi.Dim, trim: true));
        if (MediaFeatureResolver.SourceName(info.SourceAppName) is { Length: > 0 } src) { var badge = V3Ui.StatusBadge(src, BadgeTone.Accent); badge.HorizontalAlignment = HorizontalAlignment.Left; badge.Margin = new Thickness(0, 3, 0, 0); texts.Children.Add(badge); }
        if (CurrentLyric() is { Length: > 0 } lyric) texts.Children.Add(new TextBlock { Text = lyric, FontSize = 11.5, FontStyle = Windows.UI.Text.FontStyle.Italic, Foreground = new SolidColorBrush(V3Ui.AccentText), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        Grid.SetColumn(texts, 1); head.Children.Add(texts);
        body.Children.Add(head);

        if (info.Duration is { TotalSeconds: > 0 } d)
        {
            var pos = info.Position + (info.IsPlaying ? DateTime.UtcNow - _infoAtUtc : TimeSpan.Zero); var frac = Math.Clamp(pos.TotalSeconds / d.TotalSeconds, 0, 1);
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(CardUi.Text(V3Ui.Clock(pos), 11, CardUi.Dim));
            var bar = V3Ui.Progress(frac, 4); bar.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(bar, 1); row.Children.Add(bar);
            var total = CardUi.Text(V3Ui.Clock(d), 11, CardUi.Dim); Grid.SetColumn(total, 2); row.Children.Add(total);
            body.Children.Add(row);
        }

        if (_viewModel.MediaControls is not null) body.Children.Add(HomeTransport(info, caps));
        var card = V3Ui.Surface(body, 14, interactive: false);
        card.Tapped += (_, e) => { if (e.OriginalSource is FrameworkElement { Tag: "ctl" }) return; OpenSection(NotchSections.Music); };
        return card;
    }

    /// <summary>Controles de transporte: aleatorio y repetir solo si la sesión los habilita; el aleatorio se ve activo con el estado real que informa el reproductor.</summary>
    private StackPanel HomeTransport(NowPlayingInfo info, MediaCapabilities caps)
    {
        var ctl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        Button Tag(Button b) { b.Tag = "ctl"; b.Tapped += (_, e) => e.Handled = true; return b; }
        if (caps.HasFlag(MediaCapabilities.Shuffle))
        {
            bool on = info.Extras?.Shuffle == true;
            ctl.Children.Add(Tag(V3Ui.ActionButton("", on ? "Aleatorio: activado" : "Aleatorio: desactivado", () => _ = RunControl(c => c.SetShuffleAsync(!on)), on ? ActionStyle.Primary : ActionStyle.Ghost, size: 32)));
        }

        ctl.Children.Add(Tag(V3Ui.ActionButton("", "Anterior", () => _ = RunControl(c => c.PreviousAsync()), ActionStyle.Ghost, enabled: caps == MediaCapabilities.None || caps.HasFlag(MediaCapabilities.Previous), size: 36)));
        ctl.Children.Add(Tag(V3Ui.ActionButton(info.IsPlaying ? "" : "", info.IsPlaying ? "Pausar" : "Reproducir", () => _ = RunControl(c => c.TogglePlayPauseAsync()), ActionStyle.Primary, size: 46)));
        ctl.Children.Add(Tag(V3Ui.ActionButton("", "Siguiente", () => _ = RunControl(c => c.NextAsync()), ActionStyle.Ghost, enabled: caps == MediaCapabilities.None || caps.HasFlag(MediaCapabilities.Next), size: 36)));
        if (caps.HasFlag(MediaCapabilities.Repeat))
        {
            var mode = info.Extras?.Repeat ?? RepeatMode.Off; bool on = mode != RepeatMode.Off;
            ctl.Children.Add(Tag(V3Ui.ActionButton(mode == RepeatMode.Track ? "" : "", MediaFeatureResolver.RepeatLabel(mode), () => _ = RunControl(c => c.SetRepeatAsync(MediaFeatureResolver.NextRepeat(mode))), on ? ActionStyle.Primary : ActionStyle.Ghost, size: 32)));
        }

        return ctl;
    }

    // ───────────── principal: temporizador
    private UIElement HomeTimerCard()
    {
        var t = _viewModel.Timer; var phase = TimerView.Phase(TimerStates.Of(t));
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(V3Ui.Ring(t.FractionLeft, 104, FormatCountdown(t.Remaining), TimerView.Label(phase)));
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 10 };
        right.Children.Add(CardUi.Text("Temporizador", 15, Pal.Fg, bold: true));
        right.Children.Add(TimerButtons(phase));
        Grid.SetColumn(right, 1); row.Children.Add(right);
        var card = V3Ui.Surface(row, 14);
        card.Tapped += (_, e) => { if (e.OriginalSource is FrameworkElement { Tag: "ctl" }) return; OpenSection(NotchSections.Timer); };
        return card;
    }

    /// <summary>Temporizador en marcha mientras otra cosa es lo principal (música, descarga): tiempo grande, estado y los mismos botones que la pestaña.</summary>
    private UIElement HomeTimerCompact()
    {
        var t = _viewModel.Timer; var phase = TimerView.Phase(TimerStates.Of(t));
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ring = V3Ui.Ring(t.FractionLeft, 64, "", null); Grid.SetColumn(ring, 0); row.Children.Add(ring);
        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        txt.Children.Add(CardUi.Text(FormatCountdown(t.Remaining), 26, Pal.Fg, bold: true));
        txt.Children.Add(CardUi.Text("Temporizador · " + TimerView.Label(phase), 11.5, CardUi.Dim));
        Grid.SetColumn(txt, 1); row.Children.Add(txt);
        var btns = TimerButtons(phase); btns.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(btns, 2); row.Children.Add(btns);
        var card = V3Ui.Surface(row, 12, interactive: true);
        card.Tapped += (_, e) => { if (e.OriginalSource is FrameworkElement { Tag: "ctl" }) return; OpenSection(NotchSections.Timer); };
        return card;
    }

    /// <summary>Los botones salen de <see cref="TimerView.Actions"/>: lo que se puede hacer depende del estado REAL del temporizador, el mismo que ve la cápsula.</summary>
    private StackPanel TimerButtons(TimerPhase phase)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var a in TimerView.Actions(phase))
        {
            var (glyph, style) = a switch { TimerAction.Start or TimerAction.Resume => ("", ActionStyle.Primary), TimerAction.Pause => ("", ActionStyle.Primary), TimerAction.Restart => ("", ActionStyle.Secondary), _ => ("", ActionStyle.Secondary) };
            var act = a; var b = V3Ui.ActionButton(glyph, TimerView.ActionLabel(a, phase), () => ApplyTimerAction(act), style, size: 38); b.Tag = "ctl"; b.Tapped += (_, e) => e.Handled = true; s.Children.Add(b);
        }

        return s;
    }

    private void ApplyTimerAction(TimerAction a)
    {
        var t = _viewModel.Timer;
        switch (a)
        {
            case TimerAction.Start or TimerAction.Resume: t.Start(); break;
            case TimerAction.Pause: t.Pause(); break;
            case TimerAction.Restart: t.Restart(); break;
            case TimerAction.Cancel: t.Cancel(); break;
        }

        UpdateTimerUi(); RebuildHome(force: true); UpdatePillText(); ApplyLayout(animate: false);
    }

    // ───────────── principal: descarga
    private UIElement HomeDownloadCard(DownloadView.Row r)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(V3Ui.ModuleHeader(V3Ui.FileGlyph(r.Kind), r.Name, DownloadView.StateLabel(r.State) + (r.BytesPerSecond is { } bps && bps > 0 ? " · " + DownloadTracker.FormatRate(bps) : "")));
        body.Children.Add(V3Ui.Progress(r.Fraction));
        var info = new Grid();
        info.Children.Add(CardUi.Text(DownloadView.SizeText(r), 11.5, CardUi.Dim));
        if (DownloadView.EtaText(r.Remaining) is { } eta) { var e = CardUi.Text(eta, 11.5, CardUi.Dim); e.HorizontalAlignment = HorizontalAlignment.Right; info.Children.Add(e); }
        body.Children.Add(info);
        var card = V3Ui.Surface(body, 14, interactive: true); card.Tapped += (_, _) => OpenSection(NotchSections.Downloads);
        return card;
    }

    // ───────────── secundarias
    private UIElement? HomeSecondaryRow(HomeContext ctx, IReadOnlyList<DownloadView.Row> rows)
    {
        var chips = new List<UIElement>();
        foreach (var s in HomeFocus.Secondary(ctx))
        {
            switch (s)
            {
                case HomeSecondary.Notifications when SectionOn(NotchSections.Notifications): chips.Add(HomeChip(SectionInfo[NotchSections.Notifications].Glyph, "Notificaciones", ctx.Notifications > 0 ? V3Ui.StatusBadge(ctx.Notifications.ToString(), BadgeTone.Danger) : null, NotchSections.Notifications)); break;
                case HomeSecondary.Downloads when SectionOn(NotchSections.Downloads): { var r = rows.First(x => x.IsActive); chips.Add(HomeChip(SectionInfo[NotchSections.Downloads].Glyph, r.Name, V3Ui.Progress(r.Fraction, 3), NotchSections.Downloads)); break; }
                case HomeSecondary.Timer when SectionOn(NotchSections.Timer) && TimerView.Phase(TimerStates.Of(_viewModel.Timer)) == TimerPhase.Ready: chips.Add(HomeChip(SectionInfo[NotchSections.Timer].Glyph, FormatCountdown(_viewModel.Timer.Remaining), null, NotchSections.Timer)); break;
                case HomeSecondary.Music when SectionOn(NotchSections.Music) && _info is { } m: chips.Add(HomeChip(SectionInfo[NotchSections.Music].Glyph, m.Title, null, NotchSections.Music)); break;
            }
        }

        if (chips.Count == 0) return null;
        var g = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < chips.Count; i++) { g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); Grid.SetColumn((FrameworkElement)chips[i], i); g.Children.Add(chips[i]); }
        return g;
    }

    private static string DeviceGlyph(string type) => type switch { "mouse" => "\uE962", "keyboard" => "\uE765", "headset" => "\uE7F6", "gamepad" => "\uE7FC", _ => "\uE772" };

    /// <summary>Batería de los periféricos que la Barra ya leyó (solo conectados y con porcentaje real). Sin datos (Periféricos apagado en la Barra o nada conectado) la tarjeta no se muestra.</summary>
    private UIElement? HomePeripheralsCard()
    {
        if (_viewModel.Peripherals.Count == 0) return null;
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(new TextBlock { Text = "PERIFÉRICOS", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 80, Foreground = new SolidColorBrush(CardUi.Dim) });
        foreach (var d in _viewModel.Peripherals.Take(4))
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(CardUi.Icon(DeviceGlyph(d.Type), 13, V3Ui.AccentText));
            var name = CardUi.Text(d.Name, 12, Pal.Fg, trim: true); Grid.SetColumn(name, 1); row.Children.Add(name);
            var pct = CardUi.Text(d.Percent + " %" + (d.Charging ? " ⚡" : ""), 12, d.Percent <= 15 && !d.Charging ? V3Ui.Danger : Pal.Fg, bold: true); Grid.SetColumn(pct, 2); row.Children.Add(pct);
            s.Children.Add(row);
        }

        return V3Ui.Surface(s, 10);
    }

    private UIElement HomeChip(string glyph, string text, UIElement? extra, string target)
    {
        var s = new StackPanel { Spacing = 6 };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        top.Children.Add(CardUi.Icon(glyph, 14, V3Ui.AccentText));
        top.Children.Add(CardUi.Text(text, 12, Pal.Fg, bold: true, trim: true));
        if (extra is Border badge) top.Children.Add(badge);
        s.Children.Add(top);
        if (extra is ProgressBar bar) s.Children.Add(bar);
        var chip = V3Ui.Surface(s, 10, interactive: true); chip.Tapped += (_, _) => OpenSection(target);
        ToolTipService.SetToolTip(chip, SectionInfo[target].Name);
        return chip;
    }

    // ───────────── accesos rápidos (lista cerrada; los de Hatsu solo si la Barra los declara disponibles)
    private UIElement? HomeQuickRow()
    {
        var items = QuickActions.Visible(_viewModel.QuickAvailable, SectionOn(NotchSections.Clipboard) );
        if (items.Count == 0) return null;
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(new TextBlock { Text = "ACCESOS RÁPIDOS", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 80, Foreground = new SolidColorBrush(CardUi.Faint) });
        var g = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < items.Count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var q = items[i]; var tile = V3Ui.QuickTile(q.Glyph, q.Label, () => RunQuickAction(q)); Grid.SetColumn(tile, i); g.Children.Add(tile);
        }

        s.Children.Add(g);
        return s;
    }

    private void RunQuickAction(QuickAction q)
    {
        if (q.LocalOnly) { if (q.Id == "clipboard") OpenSection(NotchSections.Clipboard); return; }
        SetExpanded(false);   // la Isla se cierra primero (como antes de capturar): la acción de Hatsu actúa sobre el escritorio, no sobre la Isla
        _viewModel.RunQuick?.Invoke(q.Id);
    }

    /// <summary>Línea de letra actual (misma fuente y cuenta que el cabecero de música); vacía si no hay letra o no suena.</summary>
    private string CurrentLyric()
    {
        if (_info is { IsPlaying: true } info && _lyrics is { Count: > 0 } lines)
        {
            var pos = info.Position + (DateTime.UtcNow - _infoAtUtc);
            var i = NotchWin.Core.Lyrics.LrcParser.IndexAt(lines, pos);
            return i >= 0 && lines[i].Text.Length > 0 ? lines[i].Text : "♪";
        }

        return "";
    }
}
