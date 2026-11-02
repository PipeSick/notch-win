namespace NotchWin.App.Interop;

/// <summary>Extended-style tweaks applied to a WinUI window's HWND that the AppWindow API doesn't expose.</summary>
internal static class Win32Window
{
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmaBorderColor = 34;
    private const int DwmCornerDoNotRound = 1;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    /// <summary>
    /// Marks the window as a tool window (hidden from Alt+Tab and the taskbar) and non-activating
    /// (clicking it never steals keyboard focus from whatever app the user was using) — both
    /// expected of a notch-style overlay. Also turns off Windows 11's automatic rounded corners and
    /// 1px border, since the notch draws its own shape with <see cref="ApplyNotchShape"/>.
    /// </summary>
    public static void MakeOverlay(IntPtr hwnd)
    {
        var current = (uint)NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        var updated = current | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(updated));

        // Plain popup: no caption, border or resize frame. Otherwise Windows keeps a thin non-client
        // strip around the content that shows up as a white outline.
        NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWL_STYLE, new IntPtr(unchecked((int)NativeMethods.WS_POPUP)));
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

        var corner = DwmCornerDoNotRound;
        NativeMethods.DwmSetWindowAttribute(hwnd, DwmaWindowCornerPreference, ref corner, sizeof(int));
        var border = DwmColorNone;
        NativeMethods.DwmSetWindowAttribute(hwnd, DwmaBorderColor, ref border, sizeof(int));
    }

    /// <summary>
    /// Clips the window to a notch silhouette (macOS style): flat top edge flush with the screen, concave «shoulders» of <paramref name="shoulderPx"/> joining it to that edge,
    /// a body inset by the shoulders and rounded bottom corners. The body's top corners sit above the window so only the bottom rounds.
    /// </summary>
    public static void ApplyNotchShape(IntPtr hwnd, int widthPx, int heightPx, int radiusPx, int shoulderPx = 0)
    {
        var s = Math.Clamp(shoulderPx, 0, widthPx / 4);
        var bodyW = widthPx - 2 * s;
        var r = Math.Max(0, Math.Min(radiusPx, Math.Min(heightPx, bodyW) / 2));
        var region = NativeMethods.CreateRoundRectRgn(s, -r, s + bodyW + 1, heightPx + 1, r * 2, r * 2);
        if (s > 0 && heightPx > s)
        {
            // Each shoulder: an s×s square at the top corner minus a circle of radius s centred on its outer-bottom corner → a concave fillet down into the body.
            AddShoulder(region, 0, 0, s, -s, 0, s, 2 * s);
            AddShoulder(region, widthPx - s, 0, widthPx, widthPx - s, 0, widthPx + s, 2 * s);
        }

        // The system owns the region after a successful call, so it must not be deleted here.
        NativeMethods.SetWindowRgn(hwnd, region, true);
    }

    private static void AddShoulder(IntPtr region, int x1, int y1, int x2, int ex1, int ey1, int ex2, int ey2)
    {
        var square = NativeMethods.CreateRectRgn(x1, y1, x2, y1 + (x2 - x1));
        var circle = NativeMethods.CreateEllipticRgn(ex1, ey1, ex2, ey2);
        try
        {
            NativeMethods.CombineRgn(square, square, circle, NativeMethods.RGN_DIFF);
            NativeMethods.CombineRgn(region, region, square, NativeMethods.RGN_OR);
        }
        finally { NativeMethods.DeleteObject(square); NativeMethods.DeleteObject(circle); }
    }
}
