using NotchWin.Core.Settings;

namespace NotchWin.Core.Notch;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public bool Contains(PixelRect r) => !r.IsEmpty && r.X >= X && r.Y >= Y && r.Right <= Right && r.Bottom <= Bottom;
}

/// <summary>
/// Where a notch goes, and whether the displays have settled after a change. Pure (tested on Linux).
/// <para>
/// Why this exists: switching Windows from «Duplicate» to «Extend» (Win+P) reports the change before the layout settles. The DisplayArea handed over by that event — and,
/// for a few seconds on some drivers, <c>DisplayArea.FindAll()</c> too — can still describe the old layout (e.g. one area spanning both screens), so a notch centred on it lands
/// exactly on the seam between two monitors. The fix: dock to the monitor's LIVE rectangle from Win32 whenever it is available, and keep re-checking until every notch sits
/// inside a real monitor.
/// </para>
/// </summary>
public static class NotchPlacement
{
    /// <summary>Delays (ms after the change) of the successive re-checks. Long tail on purpose: a GPU mode switch (NVIDIA, 1440p, remote-desktop streamers) can take seconds.</summary>
    public static readonly IReadOnlyList<int> ResyncDelaysMs = [600, 1500, 3000, 5000, 8000, 12000];

    /// <summary>The monitor rectangle to dock to: the live one from Win32 (by the display's id) when Windows gives it, otherwise what the DisplayArea says.</summary>
    public static PixelRect ChooseBounds(PixelRect displayArea, PixelRect? liveMonitor) =>
        liveMonitor is { IsEmpty: false } live ? live : displayArea;

    /// <summary>The notch window rectangle on <paramref name="monitor"/>: flush with its top edge, centred or at a corner (16 DIP margin).</summary>
    public static PixelRect Dock(PixelRect monitor, int width, int height, NotchPosition position, double scale)
    {
        int margin = (int)(16 * (scale > 0 ? scale : 1));
        int x = position switch
        {
            NotchPosition.TopLeft => monitor.X + margin,
            NotchPosition.TopRight => monitor.X + monitor.Width - width - margin,
            _ => monitor.X + (monitor.Width - width) / 2,
        };
        return new PixelRect(x, monitor.Y, width, height);
    }

    /// <summary>
    /// The displays have settled when every notch's monitor rectangle is exactly one of the real monitors, and no notch window crosses a monitor edge.
    /// A rectangle spanning two screens (the half-way snapshot of a Duplicate → Extend switch) is NOT settled.
    /// </summary>
    public static bool IsSettled(IEnumerable<(PixelRect Monitor, PixelRect Window)> notches, IReadOnlyCollection<PixelRect> realMonitors)
    {
        if (realMonitors.Count == 0) return false;
        foreach (var (monitor, window) in notches)
        {
            if (!realMonitors.Contains(monitor)) return false;
            if (!window.IsEmpty && !realMonitors.Any(m => m.Contains(window))) return false;
        }

        return true;
    }

    // ── Shape: matching WinMac's top bar
    /// <summary>WinMac's top bar height (DIP, BarMetrics.Height). Inside WinMac the collapsed notch is exactly as tall, so it reads as part of the bar instead of hanging below it.</summary>
    public const int WinMacBarHeightDip = 28;
    /// <summary>Collapsed height on its own (no WinMac bar to line up with).</summary>
    public const int StandaloneCollapsedHeightDip = 32;
    /// <summary>Width of the concave «shoulders» that join the notch to the top screen edge (macOS style).</summary>
    public const double ShoulderDip = 6;

    public static int CollapsedHeightDip(bool hostedByWinMac) => hostedByWinMac ? WinMacBarHeightDip : StandaloneCollapsedHeightDip;

    /// <summary>Bottom corner radius for a given height: tight on the slim collapsed notch (10 DIP at 28), roomier when it opens (up to 20). Never a half-circle «pill».</summary>
    public static double CornerRadiusDip(double heightDip) => Math.Clamp(heightDip * 0.36, 8, 20);

    /// <summary>
    /// The notch silhouette as a few rectangles (physical pixels) for WinMac to cut out of its bar: the top strip with the shoulders, the straight part of the body and
    /// <paramref name="slices"/> slices down each rounded bottom corner. Publishing the plain window rectangle instead left uncovered wedges at the rounded corners (wallpaper
    /// showing between the bar and the notch). Each slice is sized at its widest point, so the bar never covers the notch; what remains uncovered is at most a pixel or two.
    /// </summary>
    public static IReadOnlyList<PixelRect> Silhouette(PixelRect window, int radiusPx, int shoulderPx, int slices = 3)
    {
        if (window.IsEmpty) return [];
        int s = Math.Clamp(shoulderPx, 0, window.Width / 4), bodyW = window.Width - 2 * s, r = Math.Clamp(radiusPx, 0, Math.Min(window.Height, bodyW) / 2);
        var list = new List<PixelRect>();
        if (s > 0) list.Add(new PixelRect(window.X, window.Y, window.Width, Math.Min(s, window.Height)));
        int straight = window.Height - r;
        if (straight > 0) list.Add(new PixelRect(window.X + s, window.Y, bodyW, straight));
        if (r > 0)
        {
            int n = Math.Max(1, slices);
            for (int i = 0; i < n; i++)
            {
                int y0 = straight + i * r / n, y1 = straight + (i + 1) * r / n; if (y1 <= y0) continue;
                double dy = y0 - straight; int inset = (int)Math.Floor(r - Math.Sqrt(Math.Max(0, (double)r * r - dy * dy)));
                list.Add(new PixelRect(window.X + s + inset, window.Y + y0, bodyW - 2 * inset, y1 - y0));
            }
        }

        return list;
    }

    /// <summary>
    /// Delay before the next re-check after pass <paramref name="pass"/> (0-based) finished, or null to stop.
    /// Settled: one confirming pass, then stop. Not settled: keep going along <see cref="ResyncDelaysMs"/> until it runs out.
    /// </summary>
    public static TimeSpan? NextResyncDelay(int pass, bool settled)
    {
        if (settled && pass >= 1) return null;
        int next = pass + 1;
        if (next >= ResyncDelaysMs.Count) return null;
        return TimeSpan.FromMilliseconds(ResyncDelaysMs[next] - ResyncDelaysMs[pass]);
    }
}
