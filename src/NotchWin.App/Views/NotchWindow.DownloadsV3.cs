using Activity = NotchWin.Core.Activities.Activity;
using ActivityKind = NotchWin.Core.Activities.ActivityKind;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.App.Views.V3;
using NotchWin.Core.Activities;
using NotchWin.Core.Downloads;
using NotchWin.Core.Notch.V3;
using NotchWin.Core.Settings;
using System.Diagnostics;

namespace NotchWin.App.Views;

/// <summary>
/// Descargas V3. Dos orígenes, dichos claramente: las ADMINISTRADAS por Hatsu (el usuario pega un enlace: tamaño total, tiempo restante, pausar, reanudar y cancelar reales) y las OBSERVADAS en la
/// carpeta de descargas del navegador (solo bytes y velocidad, barra indeterminada, sin control: no se promete lo que no se puede). «Completadas» muestra el historial con abrir archivo y abrir ubicación.
/// </summary>
public sealed partial class NotchWindow
{
    private bool _dlAdding, _dlShowDone;
    private string _dlNewUrl = "", _dlSignature = "";

    private static string DlSignature(IReadOnlyList<DownloadView.Row> rows, int history, bool adding, bool done) =>
        string.Join("|", rows.Select(r => r.Key + r.State + r.ReceivedBytes / 262144 + r.TotalBytes + r.Error)) + "#" + history + adding + done + Pal.Light;

    private void RebuildDownloadsV3(bool force = false)
    {
        var rows = DownloadRows(); var history = _viewModel.Activities.History.Where(a => a.Kind == ActivityKind.Download).ToList();
        var sig = DlSignature(rows, history.Count, _dlAdding, _dlShowDone);
        if (!force && sig == _dlSignature) return;
        _dlSignature = sig;

        DownloadsPane.Children.Clear();
        var active = rows.Where(r => r.IsActive).ToList(); var finished = rows.Where(r => !r.IsActive).ToList();
        var addBtn = V3Ui.ActionButton("", "Descargar un enlace con Hatsu", () => { _dlAdding = !_dlAdding; _dlNewUrl = ""; RebuildDownloadsV3(force: true); RelayoutIfExpanded(); if (_dlAdding) KeyboardOn(); }, _dlAdding ? ActionStyle.Secondary : ActionStyle.Primary, size: 30);
        DownloadsPane.Children.Add(V3Ui.ModuleHeader(SectionInfo[NotchSections.Downloads].Glyph, "Descargas", active.Count > 0 ? $"{active.Count} en curso" : "Nada en curso", addBtn));
        if (_dlAdding) DownloadsPane.Children.Add(DownloadAddForm());

        var list = new StackPanel { Spacing = 8 };
        if (active.Count == 0 && !_dlAdding && finished.Count == 0 && history.Count == 0)
            list.Children.Add(V3Ui.EmptyState("", "Sin descargas", "Las del navegador aparecen aquí mientras se descargan. Con «+» puedes descargar un enlace con Hatsu y controlarla de verdad (pausar, reanudar, cancelar).", ("Descargar un enlace", () => { _dlAdding = true; RebuildDownloadsV3(force: true); RelayoutIfExpanded(); KeyboardOn(); })));
        foreach (var r in active) list.Children.Add(DownloadRowView(r, null));

        int doneCount = finished.Count + history.Count;
        if (doneCount > 0)
        {
            var toggle = V3Ui.TextButton((_dlShowDone ? "Ocultar" : "Mostrar") + $" completadas ({doneCount})", () => { _dlShowDone = !_dlShowDone; RebuildDownloadsV3(force: true); RelayoutIfExpanded(); }, ActionStyle.Ghost);
            toggle.HorizontalAlignment = HorizontalAlignment.Left; list.Children.Add(toggle);
            if (_dlShowDone)
            {
                foreach (var r in finished) list.Children.Add(DownloadRowView(r, null));
                foreach (var a in history.Take(20)) list.Children.Add(DownloadRowView(null, a));
            }
        }

        DownloadsPane.Children.Add(new ScrollViewer { MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = list });
    }

    private UIElement DownloadAddForm()
    {
        var s = new StackPanel { Spacing = 8 };
        var url = new TextBox { PlaceholderText = "Pega un enlace http o https", Text = _dlNewUrl, CornerRadius = new CornerRadius(10) };
        url.TextChanged += (_, _) => _dlNewUrl = url.Text;
        s.Children.Add(url);
        var msg = new TextBlock { FontSize = 11.5, Foreground = new SolidColorBrush(V3Ui.Danger), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed }; s.Children.Add(msg);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(V3Ui.TextButton("Descargar", () =>
        {
            if (_viewModel.Managed?.Add(_dlNewUrl) is null) { msg.Text = "Escribe un enlace http o https válido."; msg.Visibility = Visibility.Visible; return; }
            _dlAdding = false; _dlNewUrl = ""; RebuildDownloadsV3(force: true); RelayoutIfExpanded();
        }, ActionStyle.Primary));
        // sugerencia SOLO con lo que ya está en el historial del portapapeles (no se lee nada nuevo) y siempre como una acción del usuario
        var copied = _viewModel.Clipboard.All.FirstOrDefault(e => e.Kind == NotchWin.Core.Clipboard.ClipboardItemKind.Text && NotchWin.Core.Notch.V3.ClipClassifier.Classify(e.Text) == ClipKind.Link);
        if (copied?.Text is { } link && NotchWin.Core.Notch.V3.ClipClassifier.LinkHost(link) is { } host)
            row.Children.Add(V3Ui.TextButton("Usar el último enlace copiado (" + host + ")", () => { _dlNewUrl = link.Trim(); RebuildDownloadsV3(force: true); RelayoutIfExpanded(); }, ActionStyle.Ghost));
        s.Children.Add(row);
        return V3Ui.Surface(s, 10);
    }

    /// <summary>Una fila: de la lista unificada (<paramref name="r"/>) o de una actividad completada del historial (<paramref name="a"/>).</summary>
    private UIElement DownloadRowView(DownloadView.Row? r, Activity? a)
    {
        string name = r?.Name ?? a!.Title; var kind = DownloadView.Classify(name);
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(V3Ui.Tile(V3Ui.FileGlyph(kind), 38));
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
        mid.Children.Add(CardUi.Text(name, 12.5, Pal.Fg, bold: true, trim: true));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        if (r is not null)
        {
            var parts = new List<string> { DownloadView.SizeText(r) };
            if (r.IsActive && r.BytesPerSecond is { } bps && bps > 0) parts.Add(DownloadTracker.FormatRate(bps));
            if (DownloadView.EtaText(r.Remaining) is { } eta) parts.Add(eta);
            mid.Children.Add(CardUi.Text(string.Join(" · ", parts), 11, CardUi.Dim, trim: true));
            if (r.IsActive) mid.Children.Add(V3Ui.Progress(r.Fraction, 4));
            var info = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            info.Children.Add(V3Ui.StatusBadge(r.IsManaged ? DownloadView.StateLabel(r.State) : "Descargando (observada)", r.State switch { ManagedState.Completed => BadgeTone.Success, ManagedState.Failed => BadgeTone.Danger, ManagedState.Cancelled => BadgeTone.Neutral, ManagedState.Paused => BadgeTone.Warning, _ => BadgeTone.Accent }));
            if (r.Error is { Length: > 0 } err) info.Children.Add(CardUi.Text(err, 10.5, V3Ui.Danger, trim: true));
            mid.Children.Add(info);
            var id = r.ManagedId;
            if (r.CanPause) buttons.Children.Add(V3Ui.ActionButton("", "Pausar", () => _viewModel.Managed?.Pause(id!), ActionStyle.Ghost, size: 30));
            if (r.CanResume) buttons.Children.Add(V3Ui.ActionButton("", r.State == ManagedState.Failed ? "Reintentar" : "Reanudar", () => _viewModel.Managed?.Resume(id!), ActionStyle.Ghost, size: 30));
            if (r.CanCancel) buttons.Children.Add(V3Ui.ActionButton("", "Cancelar la descarga", () => _viewModel.Managed?.Cancel(id!), ActionStyle.Ghost, size: 30));
            if (r.CanOpen) { var path = r.Path!; buttons.Children.Add(V3Ui.ActionButton("", "Abrir archivo", () => OpenFile(path), ActionStyle.Ghost, size: 30)); buttons.Children.Add(V3Ui.ActionButton("", "Abrir ubicación", () => ShowInFolder(path), ActionStyle.Ghost, size: 30)); }
            if (r.IsManaged && !r.IsActive) buttons.Children.Add(V3Ui.ActionButton("", "Quitar de la lista (no borra el archivo)", () => _viewModel.Managed?.Remove(id!), ActionStyle.Ghost, size: 30));
        }
        else
        {
            mid.Children.Add(CardUi.Text((a!.Subtitle is { Length: > 0 } sub ? sub + " · " : "") + DownloadView.StateLabel(a.State), 11, CardUi.Dim, trim: true));
            if (a.Path is { } p && File.Exists(p)) { buttons.Children.Add(V3Ui.ActionButton("", "Abrir archivo", () => _ = RunAction(a, ActivityAction.Open), ActionStyle.Ghost, size: 30)); buttons.Children.Add(V3Ui.ActionButton("", "Abrir ubicación", () => _ = RunAction(a, ActivityAction.ShowInFolder), ActionStyle.Ghost, size: 30)); }
        }

        Grid.SetColumn(mid, 1); grid.Children.Add(mid); Grid.SetColumn(buttons, 2); grid.Children.Add(buttons);
        return V3Ui.Surface(grid, 10);
    }

    private static void OpenFile(string path) { try { if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); } catch (Exception) { /* un archivo que ya no está: no pasa nada */ } }

    private static void ShowInFolder(string path) { try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose(); } catch (Exception) { } }
}
