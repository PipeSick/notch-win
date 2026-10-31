using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotchWin.App.Modules.Resources;
using NotchWin.Core.Settings;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>The "system resources" tab: CPU, GPU, memory, battery, network, disk, power draw and connected devices.</summary>
public sealed partial class NotchWindow
{
    private sealed class MetricCard
    {
        public required TextBlock Value { get; init; }

        public TextBlock? Sub { get; init; }

        public Border? BarHost { get; init; }
    }

    private readonly SystemSampler _sampler = new();
    private readonly Dictionary<string, MetricCard> _metrics = [];
    private int _systemSamples;
    private bool _sampling;

    private static readonly Color BarColor = Color.FromArgb(255, 0xF2, 0xF2, 0xF7);

    private void BuildSystemCards()
    {
        for (var i = 0; i < 6; i++)
        {
            SystemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < 3; i++)
        {
            SystemGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        AddMetric("cpu", "\uE950", "CPU", 0, 0, 2, withBar: true);
        AddMetric("gpu", "\uE7F4", "GPU", 2, 0, 2, withBar: true);
        AddMetric("mem", "\uEEA1", "Memoria", 4, 0, 2, withBar: true);
        AddMetric("bat", "\uE83F", "Batería", 0, 1, 2, withBar: true);
        AddMetric("net", "\uE968", "Red", 2, 1, 2, withSub: true);
        AddMetric("disk", "\uEDA2", "Disponible", 4, 1, 2, withBar: true);
        AddMetric("power", "\uE945", "Energía", 0, 2, 3);
        AddMetric("dev", "\uE772", "Dispositivos conectados", 3, 2, 3);
    }

    private void AddMetric(string key, string glyph, string label, int column, int row, int span, bool withBar = false, bool withSub = false)
    {
        var body = new StackPanel { Spacing = 6 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(CardUi.Icon(glyph, 12, CardUi.Dim));
        head.Children.Add(CardUi.Text(label, 12, CardUi.Dim, bold: true, trim: true));
        body.Children.Add(head);

        var value = CardUi.Text("—", withSub ? 19 : 26, Colors.White, bold: true);
        body.Children.Add(value);

        TextBlock? sub = null;
        if (withSub)
        {
            sub = CardUi.Text("↑ —", 11, CardUi.Dim);
            body.Children.Add(sub);
        }

        Border? barHost = null;
        if (withBar)
        {
            barHost = new Border { Child = CardUi.Bar(0, BarColor, height: 5) };
            body.Children.Add(barHost);
        }

        var card = CardUi.Card(body);
        CardUi.At(card, column, row, span);
        SystemGrid.Children.Add(card);
        _metrics[key] = new MetricCard { Value = value, Sub = sub, BarHost = barHost };
    }

    /// <summary>Called by the 1 s clock; only does work while the tab is on screen, so the app is idle otherwise.</summary>
    private async void UpdateSystemCards()
    {
        if (!_expanded || _customizing || _active != NotchSections.Resources || _sampling)
        {
            return;
        }

        // The GPU counters can take a moment the first time, so sample off the UI thread.
        _sampling = true;
        SystemSnapshot s;
        try
        {
            s = await Task.Run(_sampler.Sample);
        }
        finally
        {
            _sampling = false;
        }

        if (!_expanded || _customizing || _active != NotchSections.Resources)
        {
            return; // the user left the tab while the reading was being taken
        }

        if (++_systemSamples == 1)
        {
            return; // CPU and network are rates: the first reading has nothing to compare against
        }

        SetPercent("cpu", s.Cpu);
        SetPercent("gpu", s.Gpu);
        SetPercent("mem", s.Memory);

        if (s.BatteryPercent is { } battery)
        {
            SetPercent("bat", battery);
        }
        else
        {
            _metrics["bat"].Value.Text = "Sin batería";
            _metrics["bat"].Value.FontSize = 17;
            _metrics["bat"].BarHost!.Child = CardUi.Bar(0, BarColor, height: 5);
        }

        _metrics["net"].Value.Text = "↓ " + FormatRate(s.DownBytesPerSec);
        _metrics["net"].Sub!.Text = "↑ " + FormatRate(s.UpBytesPerSec);

        var disk = _metrics["disk"];
        disk.Value.Text = $"{s.FreeDiskGb:0} GB";
        disk.BarHost!.Child = CardUi.Bar(DiskFreeFraction(), BarColor, height: 5);

        _metrics["power"].Value.Text = s.PowerWatts is { } w ? $"{w:0}W" : "—";
        _metrics["dev"].Value.Text = s.ConnectedDevices is { } n ? n.ToString(CultureInfo.CurrentCulture) : "—";
    }

    private void SetPercent(string key, double? value)
    {
        var metric = _metrics[key];
        metric.Value.FontSize = 26;
        metric.Value.Text = value is { } v ? $"{v:0}%" : "—";
        if (metric.BarHost is not null)
        {
            metric.BarHost.Child = CardUi.Bar((value ?? 0) / 100, BarColor, height: 5);
        }
    }

    private static double DiskFreeFraction()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            return drive.TotalSize == 0 ? 0 : (double)drive.AvailableFreeSpace / drive.TotalSize;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    private static string FormatRate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => (bytesPerSecond / 1024 / 1024).ToString("0.#", CultureInfo.CurrentCulture) + " MB/s",
        >= 1024 => (bytesPerSecond / 1024).ToString("0.#", CultureInfo.CurrentCulture) + " KB/s",
        _ => bytesPerSecond.ToString("0", CultureInfo.CurrentCulture) + " B/s",
    };
}
