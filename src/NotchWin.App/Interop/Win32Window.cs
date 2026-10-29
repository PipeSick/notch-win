namespace NotchWin.App.Interop;

/// <summary>Extended-style tweaks applied to a WinUI window's HWND that the AppWindow API doesn't expose.</summary>
internal static class Win32Window
{
    /// <summary>
    /// Marks the window as a tool window (hidden from Alt+Tab and the taskbar) and non-activating
    /// (clicking it never steals keyboard focus from whatever app the user was using) — both
    /// expected of a notch-style overlay.
    /// </summary>
    public static void MakeOverlay(IntPtr hwnd)
    {
        var current = (uint)NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        var updated = current | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(updated));
    }
}
