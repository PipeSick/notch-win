using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NotchWin.App.Interop;
using NotchWin.Core.Settings;
using Windows.Graphics;
using WinRT.Interop;

namespace NotchWin.App.Modules.Notch;

/// <summary>
/// Turns a plain WinUI <see cref="Window"/> into the notch overlay: borderless, always-on-top,
/// hidden from Alt+Tab/the taskbar, never steals focus, and docked flush to the top of one monitor.
/// The OS window *is* the notch silhouette (clipped with a window region), and <see cref="AnimateTo"/>
/// eases its size frame by frame so it grows out of the screen edge like a Dynamic Island.
/// </summary>
public sealed class NotchWindowController
{
    private const int CornerRadiusDip = 20;
    private const int AnimationMs = 260;

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly NotchPosition _position;
    private readonly DisplayArea _displayArea;
    private readonly DispatcherQueueTimer _timer;

    private double _fromW, _fromH, _toW, _toH, _curW, _curH;
    private DateTime _startedUtc;
    private Action? _onCompleted;

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

        _timer = window.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(8);
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>Jumps to a size with no animation (first show, or when animations don't make sense).</summary>
    public void SnapTo(int widthDip, int heightDip)
    {
        _timer.Stop();
        _onCompleted = null;
        var scale = Scale();
        _curW = _toW = widthDip * scale;
        _curH = _toH = heightDip * scale;
        ApplyFrame();
    }

    /// <summary>Eases the window to a new size (in DIPs); <paramref name="completed"/> runs once it lands.</summary>
    public void AnimateTo(int widthDip, int heightDip, Action? completed = null)
    {
        var scale = Scale();
        _fromW = _curW;
        _fromH = _curH;
        _toW = widthDip * scale;
        _toH = heightDip * scale;
        _startedUtc = DateTime.UtcNow;
        _onCompleted = completed;
        _timer.Start();
    }

    private void OnTick()
    {
        var t = Math.Clamp((DateTime.UtcNow - _startedUtc).TotalMilliseconds / AnimationMs, 0, 1);
        // Ease-out-quint: fast out of the gate, settles softly — reads as "snappy but smooth".
        var e = 1 - Math.Pow(1 - t, 5);
        _curW = _fromW + (_toW - _fromW) * e;
        _curH = _fromH + (_toH - _fromH) * e;
        ApplyFrame();

        if (t >= 1)
        {
            _timer.Stop();
            var done = _onCompleted;
            _onCompleted = null;
            done?.Invoke();
        }
    }

    /// <summary>
    /// The target monitor's scale factor, read from the monitor itself. (An earlier version parked the window at the
    /// monitor's corner before asking the window for its DPI; doing that on every resize made the notch jump.)
    /// </summary>
    private double Scale()
    {
        var bounds = _displayArea.OuterBounds;
        var center = new NativeMethods.POINT { X = bounds.X + bounds.Width / 2, Y = bounds.Y + bounds.Height / 2 };
        var monitor = NativeMethods.MonitorFromPoint(center, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero && NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
        {
            _scale = dpiX / 96.0;
        }
        else if (_scale <= 0)
        {
            _scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        }

        return _scale;
    }

    private void ApplyFrame()
    {
        var width = Math.Max(1, (int)Math.Round(_curW));
        var height = Math.Max(1, (int)Math.Round(_curH));

        // OuterBounds, not WorkArea: a notch hugs the physical screen edge even when another
        // app-bar (or the taskbar at the top) reserves space there.
        var bounds = _displayArea.OuterBounds;
        var margin = (int)(16 * _scale);

        var x = _position switch
        {
            NotchPosition.TopLeft => bounds.X + margin,
            NotchPosition.TopRight => bounds.X + bounds.Width - width - margin,
            _ => bounds.X + (bounds.Width - width) / 2,
        };

        var rect = new RectInt32(x, bounds.Y, width, height);
        if (rect.X == _lastRect.X && rect.Y == _lastRect.Y && rect.Width == _lastRect.Width && rect.Height == _lastRect.Height)
        {
            return; // nothing moved: skip the move and the region reset, which each force a repaint
        }

        _lastRect = rect;
        _appWindow.MoveAndResize(rect);

        var radius = (int)(CornerRadiusDip * _scale);
        Win32Window.ApplyNotchShape(_hwnd, width, height, radius);
    }

    private double _scale;
    private RectInt32 _lastRect;
}
