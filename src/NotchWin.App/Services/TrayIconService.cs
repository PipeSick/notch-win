using System.Runtime.InteropServices;
using NotchWin.App.Interop;

namespace NotchWin.App.Services;

/// <summary>
/// Owns the notch's system tray icon and its right-click context menu, using raw Shell_NotifyIcon
/// rather than a third-party package. The icon needs an HWND to receive click callbacks on, so this
/// creates a small invisible native window on the UI thread; WinUI's own message pump (started by
/// Application.Start) dispatches messages to it automatically since it runs on the same thread.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const string WindowClassName = "NotchWinTrayWindow";
    private const uint TrayCallbackMessage = NativeMethods.WM_APP + 1;
    private const uint MenuItemSettings = 1;
    private const uint MenuItemExit = 2;

    // Kept alive for the process lifetime: native code holds a raw pointer to this delegate's
    // thunk, so letting it be garbage-collected would crash the next time Windows calls back in.
    private readonly NativeMethods.WndProcDelegate _wndProc;

    private IntPtr _hwnd;
    private bool _iconAdded;

    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconService(string tooltip)
    {
        _wndProc = WndProc;
        _hwnd = CreateMessageWindow();
        AddIcon(tooltip);
    }

    private IntPtr CreateMessageWindow()
    {
        var hInstance = NativeMethods.GetModuleHandleW(null);
        var wndClass = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = WindowClassName,
        };

        NativeMethods.RegisterClassExW(ref wndClass);

        return NativeMethods.CreateWindowExW(
            0, WindowClassName, "NotchWin Tray", 0,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    private void AddIcon(string tooltip)
    {
        var icon = ExtractOwnExeIcon();
        var data = new NativeMethods.NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = icon,
            szTip = tooltip.Length < 128 ? tooltip : tooltip[..127],
        };

        _iconAdded = NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref data);
    }

    /// <summary>Updates the tooltip shown on hover, e.g. with a one-line usage summary.</summary>
    public void UpdateTooltip(string tooltip)
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = new NativeMethods.NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NativeMethods.NIF_TIP,
            szTip = tooltip.Length < 128 ? tooltip : tooltip[..127],
        };

        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref data);
    }

    private static IntPtr ExtractOwnExeIcon()
    {
        var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
        var large = new IntPtr[1];
        NativeMethods.ExtractIconExW(exePath, 0, large, null, 1);
        return large[0];
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case TrayCallbackMessage:
                var pointerMessage = (uint)(lParam.ToInt64() & 0xFFFF);
                if (pointerMessage is NativeMethods.WM_LBUTTONUP or NativeMethods.WM_RBUTTONUP)
                {
                    ShowContextMenu();
                }

                return IntPtr.Zero;

            case NativeMethods.WM_COMMAND:
                var commandId = (uint)(wParam.ToInt64() & 0xFFFF);
                if (commandId == MenuItemSettings)
                {
                    SettingsRequested?.Invoke();
                }
                else if (commandId == MenuItemExit)
                {
                    ExitRequested?.Invoke();
                }

                return IntPtr.Zero;

            case NativeMethods.WM_DESTROY:
                return IntPtr.Zero;

            default:
                return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    private void ShowContextMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        try
        {
            NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, MenuItemSettings, "Ajustes...");
            NativeMethods.AppendMenuW(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, MenuItemExit, "Salir");

            NativeMethods.GetCursorPos(out var cursor);

            // A popup menu only closes on an outside click if its owner window is foreground;
            // our message-only window is invisible, so this nudge is required even though the
            // window itself never becomes visually active.
            NativeMethods.SetForegroundWindow(_hwnd);

            var selected = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
                cursor.X, cursor.Y, _hwnd, IntPtr.Zero);

            if (selected != 0)
            {
                NativeMethods.PostMessageW(_hwnd, NativeMethods.WM_COMMAND, new IntPtr(selected), IntPtr.Zero);
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_iconAdded)
        {
            var data = new NativeMethods.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = 1,
            };
            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref data);
            _iconAdded = false;
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
