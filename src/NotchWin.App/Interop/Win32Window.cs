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
    /// Clips the window to a notch silhouette: flat, square top edge flush with the screen and
    /// rounded bottom corners. The region's top corners sit above the window so only the bottom rounds.
    /// </summary>
    public static void ApplyNotchShape(IntPtr hwnd, int widthPx, int heightPx, int radiusPx)
    {
        var r = Math.Min(radiusPx, heightPx / 2);
        var region = NativeMethods.CreateRoundRectRgn(0, -r, widthPx + 1, heightPx + 1, r * 2, r * 2);
        // The system owns the region after a successful call, so it must not be deleted here.
        NativeMethods.SetWindowRgn(hwnd, region, true);
    }
}
