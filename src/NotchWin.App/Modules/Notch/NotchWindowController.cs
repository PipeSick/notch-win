using Microsoft.UI;
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
    private readonly DisplayArea _displayArea;

    public bool IsExpanded { get; private set; }

    public NotchWindowController(Window window, NotchPosition position, DisplayArea displayArea)
    {
        _position = position;
        _displayArea = displayArea;
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

        _appWindow.IsShownInSwitchers = false;
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
        // OuterBounds, not WorkArea: a notch hugs the physical screen edge even when another
        // app-bar (or the taskbar at the top) reserves space there.
        var workArea = _displayArea.OuterBounds;

        // Park the window on the target monitor first so GetDpiForWindow reports that monitor's DPI.
        _appWindow.Move(new PointInt32(workArea.X, workArea.Y));
        var scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        var width = (int)(widthDip * scale);
        var height = (int)(heightDip * scale);

        var x = _position switch
        {
            NotchPosition.TopLeft => workArea.X + (int)(16 * scale),
            NotchPosition.TopRight => workArea.X + workArea.Width - width - (int)(16 * scale),
            _ => workArea.X + (workArea.Width - width) / 2,
        };

        _appWindow.MoveAndResize(new RectInt32(x, workArea.Y, width, height));
    }
}
