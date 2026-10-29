using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NotchWin.App.Interop;
using NotchWin.Core.Settings;
using Windows.Graphics;
using WinRT.Interop;

namespace NotchWin.App.Modules.Notch;

/// <summary>
/// Turns a plain WinUI <see cref="Window"/> into the notch overlay: borderless, always-on-top,
/// hidden from Alt+Tab/the taskbar, never steals focus, and docked to the top of the primary
/// monitor. <see cref="Expand"/>/<see cref="Collapse"/> resize the actual OS window rather than
/// animating opacity/clipping inside one big transparent window, so there's no need for
/// per-pixel click-through hit-testing: when collapsed, the window simply isn't over the rest of
/// the desktop.
/// </summary>
public sealed class NotchWindowController
{
    private const int CollapsedWidth = 220;
    private const int CollapsedHeight = 36;
    private const int ExpandedWidth = 420;
    private const int ExpandedHeight = 260;

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly NotchPosition _position;

    public bool IsExpanded { get; private set; }

    public NotchWindowController(Window window, NotchPosition position)
    {
        _position = position;
        _hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        }

        _appWindow.IsShownInSwitcher = false;
        Win32Window.MakeOverlay(_hwnd);

        Collapse();
    }

    public void Collapse()
    {
        IsExpanded = false;
        Resize(CollapsedWidth, CollapsedHeight);
    }

    public void Expand()
    {
        IsExpanded = true;
        Resize(ExpandedWidth, ExpandedHeight);
    }

    private void Resize(int widthDip, int heightDip)
    {
        var scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        var width = (int)(widthDip * scale);
        var height = (int)(heightDip * scale);

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var x = _position switch
        {
            NotchPosition.TopLeft => workArea.X + (int)(16 * scale),
            NotchPosition.TopRight => workArea.X + workArea.Width - width - (int)(16 * scale),
            _ => workArea.X + (workArea.Width - width) / 2,
        };

        _appWindow.MoveAndResize(new RectInt32(x, workArea.Y, width, height));
    }
}
