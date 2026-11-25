using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.App.Views.V3;
using NotchWin.Core.Notch.V3;
using NotchWin.Core.Notifications;
using NotchWin.Core.Settings;

namespace NotchWin.App.Views;

/// <summary>
/// Notificaciones V3: agrupadas por aplicación, con filtros («Todas», «Apps» de Windows, «Hatsu») e historial de la sesión. «Sin ver» es un estado LOCAL de la Isla (Windows no dice qué leíste) y
/// el historial vive solo en memoria. Lo que se puede hacer es lo que hay de verdad: abrir la app que envió el aviso (si Windows dice cuál) y «Limpiar», que solo oculta en la Isla.
/// No hay «responder» (Windows no lo permite desde fuera).
/// </summary>
public sealed partial class NotchWindow
{
    private NotificationFilter _notifFilter = NotificationFilter.All;
    private bool _notifHistory;

    private void RebuildNotificationsV3()
    {
        NotificationsPane.Children.Clear();
        var all = _viewModel.Notifications.Items; var inbox = _viewModel.Inbox; int fresh = inbox.NewCount(all);
        var clear = V3Ui.TextButton("Limpiar", () => _viewModel.Notifications.Clear(), ActionStyle.Ghost, enabled: all.Count > 0);
        ToolTipService.SetToolTip(clear, "Las oculta solo en la Isla; no las borra de Windows");
        NotificationsPane.Children.Add(V3Ui.ModuleHeader(SectionInfo[NotchSections.Notifications].Glyph, "Notificaciones", fresh > 0 ? $"{fresh} sin ver aquí" : all.Count > 0 ? "Todo visto en la Isla" : null, clear));

        var names = new List<string> { "Todas", "Apps", "Hatsu", "Historial" };
        NotificationsPane.Children.Add(V3Ui.SegmentedControl(names, _notifHistory ? 3 : (int)_notifFilter, i =>
        {
            if (i == 3) _notifHistory = true; else { _notifHistory = false; _notifFilter = (NotificationFilter)i; }
            RebuildNotificationsV3(); RelayoutIfExpanded();
        }));

        var source = _notifHistory ? inbox.History : NotificationView.Filter(all, _notifFilter);
        if (source.Count == 0)
        {
            NotificationsPane.Children.Add(_notifHistory
                ? V3Ui.EmptyState("", "Sin historial todavía", "Aquí quedan, solo mientras Hatsu esté abierto, los avisos que ya no están en el centro de Windows.")
                : all.Count == 0 ? V3Ui.EmptyState(SectionInfo[NotchSections.Notifications].Glyph, "Sin notificaciones", "Los avisos de Windows aparecerán aquí. Si no llegan, revisa los permisos de notificaciones de Hatsu Desktop.")
                : V3Ui.EmptyState("", "Nada con este filtro", "Prueba con «Todas»."));
            return;
        }

        var list = new StackPanel { Spacing = 10 };
        foreach (var g in inbox.GroupByApp(source)) list.Children.Add(NotificationGroupView(g));
        NotificationsPane.Children.Add(new ScrollViewer { MaxHeight = 262, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = list });

        if (!_notifHistory)
        {   // al verlos un momento en la Isla pasan a «vistos» (estado local); los marcadores de esta pasada se conservan hasta que se vuelva a abrir
            var shown = source.ToList();
            _ = Task.Delay(1500).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
            {
                if (_closed || !_expanded || NotificationsPane.Visibility != Visibility.Visible || _notifHistory) return;
                inbox.MarkSeen(shown); RebuildHome();
            }));
        }
    }

    private UIElement NotificationGroupView(NotificationGroup g)
    {
        var s = new StackPanel { Spacing = 6 };
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(ColorForSource(g.App)), Child = new TextBlock { Text = g.App.Length > 0 ? g.App[..1].ToUpperInvariant() : "?", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Colors.Black), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        head.Children.Add(tile);
        var name = CardUi.Text(g.App + (g.Items.Count > 1 ? $"  ·  {g.Items.Count}" : ""), 12.5, Pal.Fg, bold: true, trim: true); Grid.SetColumn(name, 1); head.Children.Add(name);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (g.NewCount > 0 && !_notifHistory) right.Children.Add(V3Ui.StatusBadge(g.NewCount + (g.NewCount == 1 ? " nueva" : " nuevas"), BadgeTone.Accent));
        if (g.AppId is { Length: > 0 } aumid) right.Children.Add(V3Ui.ActionButton("", "Abrir " + g.App, () => _ = OpenNotificationApp(aumid), ActionStyle.Ghost, size: 28));
        Grid.SetColumn(right, 2); head.Children.Add(right);
        s.Children.Add(head);
        foreach (var n in g.Items) s.Children.Add(NotificationRow(n));
        return s;
    }

    private UIElement NotificationRow(NotchNotification n)
    {
        bool open = n.Key is not null && n.Key == _openNotif; bool isNew = !_notifHistory && _viewModel.Inbox.IsNew(n);
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0), Background = new SolidColorBrush(isNew ? V3Ui.AccentText : Colors.Transparent) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(CardUi.Text(n.Title, 12.5, Pal.Fg, bold: true, trim: !open));
        if (open)
        {
            text.Children.Add(new TextBlock { Text = n.Message, FontSize = 11.5, Foreground = new SolidColorBrush(CardUi.Dim), TextWrapping = TextWrapping.WrapWholeWords, IsTextSelectionEnabled = true, Margin = new Thickness(0, 3, 0, 0) });
            if (n.AppId is { Length: > 0 } aumid) { var b = V3Ui.TextButton("Abrir " + n.Source, () => _ = OpenNotificationApp(aumid), ActionStyle.Secondary); b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 6, 0, 0); text.Children.Add(b); }
        }
        else text.Children.Add(CardUi.Text(n.Message, 11.5, CardUi.Dim, trim: true));
        Grid.SetColumn(text, 1); row.Children.Add(text);
        var ago = CardUi.Text(NotificationView.Relative(n.At, DateTimeOffset.Now), 10.5, CardUi.Faint); ago.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(ago, 2); row.Children.Add(ago);
        var card = V3Ui.Surface(row, 9, interactive: true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(card, (isNew ? "Nueva. " : "") + n.Source + ": " + n.Title);
        if (n.Key is { } key) card.Tapped += (_, e) => { if (e.OriginalSource is Button || (e.OriginalSource is FrameworkElement { Parent: Button })) return; _openNotif = _openNotif == key ? null : key; _viewModel.Inbox.MarkSeen([n]); RebuildNotificationsV3(); RelayoutIfExpanded(); };
        return card;
    }
}
