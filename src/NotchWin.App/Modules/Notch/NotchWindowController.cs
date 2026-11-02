using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NotchWin.App.Interop;
using NotchWin.Core.Notch;
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
public sealed class NotchWindowController : IDisposable
{
    private const int AnimationMs = 260;

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly NotchPosition _position;
    private readonly DispatcherQueueTimer _timer;
    private DisplayArea _displayArea;

    private double _fromW, _fromH, _toW, _toH, _curW, _curH;
    private DateTime _startedUtc;
    private Action? _onCompleted;

    private double _scale;
    private RectInt32 _lastRect;
    private bool _disposed;

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

    /// <summary>The monitor's width in DIPs, so parts of the notch that grow on their own can stay proportionate on small screens.</summary>
    public double MonitorWidthDip => Bounds().Width / (_scale > 0 ? _scale : 1);

    /// <summary>The monitor rectangle this notch docks to right now (live from Win32 when available).</summary>
    public PixelRect MonitorBounds => Bounds();

    /// <summary>Where the window was last put (physical pixels); empty before the first frame.</summary>
    public PixelRect WindowRect => new(_lastRect.X, _lastRect.Y, _lastRect.Width, _lastRect.Height);

    /// <summary>
    /// The monitor to dock to. The DisplayArea passed in by a display-change event can be a half-way snapshot (Duplicate → Extend left it spanning both screens, so a centred
    /// notch sat on the seam). Its DisplayId still names the right monitor, so the monitor's CURRENT rectangle is read from Win32 each time; the snapshot is only a fallback.
    /// </summary>
    private PixelRect Bounds()
    {
        var ob = _displayArea.OuterBounds;
        var area = new PixelRect(ob.X, ob.Y, ob.Width, ob.Height);
        PixelRect? live = null;
        try { live = NativeMethods.MonitorRect(Win32Interop.GetMonitorFromDisplayId(_displayArea.DisplayId)); }
        catch (Exception) { /* the display is going away: use the snapshot until the next re-check */ }
        return NotchPlacement.ChooseBounds(area, live);
    }

    private RectInt32 _footprint;
    private bool _footprintAnimating;

    private int RadiusPx(int heightPx) => (int)Math.Round(NotchPlacement.CornerRadiusDip(heightPx / (_scale > 0 ? _scale : 1)) * (_scale > 0 ? _scale : 1));
    private int ShoulderPx() => (int)Math.Round(NotchPlacement.ShoulderDip * (_scale > 0 ? _scale : 1));

    /// <summary>Raised when <see cref="Footprint"/> changes (not on every animation frame).</summary>
    public event Action? FootprintChanged;

    /// <summary>
    /// The screen space (pixels) this window occupies or is about to occupy: while it animates, the union of where it started and where it is going; at rest, its current rectangle.
    /// WinMac's top bar uses it to leave that space free.
    /// </summary>
    public RectInt32 Footprint => _footprint;

    /// <summary>
    /// The same space as <see cref="Footprint"/>, but following the notch's silhouette (shoulders, body, rounded corners) when at rest, so WinMac's bar fills the corners
    /// instead of leaving the wallpaper showing there. While animating, the plain union rectangle.
    /// </summary>
    public IReadOnlyList<RectInt32> FootprintParts { get; private set; } = [];

    private void UpdateFootprint(bool animating)
    {
        RectInt32 now = ComputeRect(Math.Max(1, (int)Math.Round(_curW)), Math.Max(1, (int)Math.Round(_curH)));
        if (animating)
        {
            var a = ComputeRect(Math.Max(1, (int)Math.Round(_fromW)), Math.Max(1, (int)Math.Round(_fromH)));
            var b = ComputeRect(Math.Max(1, (int)Math.Round(_toW)), Math.Max(1, (int)Math.Round(_toH)));
            int x1 = Math.Min(a.X, b.X), y1 = Math.Min(a.Y, b.Y), x2 = Math.Max(a.X + a.Width, b.X + b.Width), y2 = Math.Max(a.Y + a.Height, b.Y + b.Height);
            now = new RectInt32(x1, y1, x2 - x1, y2 - y1);
        }

        if (now.X != _footprint.X || now.Y != _footprint.Y || now.Width != _footprint.Width || now.Height != _footprint.Height || animating != _footprintAnimating)
        {
            _footprint = now; _footprintAnimating = animating;
            FootprintParts = animating
                ? [now]
                : NotchPlacement.Silhouette(new PixelRect(now.X, now.Y, now.Width, now.Height), RadiusPx(now.Height), ShoulderPx()).Select(p => new RectInt32(p.X, p.Y, p.Width, p.Height)).ToList();
            FootprintChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
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
        UpdateFootprint(animating: false);
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
        UpdateFootprint(animating: true);
        _timer.Start();
    }

    /// <summary>
    /// The monitor's bounds changed (resolution, arrangement): dock to the new bounds at the current size.
    /// A running animation keeps going, and its completion callback still fires.
    /// </summary>
    /// <summary>
    /// Re-asserts the notch's place at the front of the always-on-top windows. Another topmost window (WinMac's top bar when running inside it) can end up above the notch;
    /// this puts it back without moving, resizing or activating anything (the DPI and position rules of this controller stay untouched).
    /// </summary>
    public void BringToFront() =>
        NativeMethods.SetWindowPos(_hwnd, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

    /// <summary>Where the window really is now (Windows may have moved it without any event).</summary>
    public PixelRect ActualRect
    {
        get
        {
            var p = _appWindow.Position; var s = _appWindow.Size;
            return new PixelRect(p.X, p.Y, s.Width, s.Height);
        }
    }

    /// <summary>False while animating (the window is between sizes on purpose).</summary>
    public bool AtRest => !_timer.IsRunning;

    /// <summary>Puts the window back on its monitor at its current size (same path as a display change), without animating.</summary>
    public void Reassert()
    {
        if (_disposed) return;
        _lastRect = default;
        ApplyFrame();
        UpdateFootprint(_timer.IsRunning);
    }

    public void UpdateDisplay(DisplayArea area)
    {
        if (_disposed)
        {
            return;
        }

        _displayArea = area;
        _lastRect = default; // force the move even if the numbers happen to match
        ApplyFrame();
        UpdateFootprint(_timer.IsRunning);
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
            UpdateFootprint(animating: false);
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
        var bounds = Bounds();
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

    /// <summary>Where the window sits for a given size: docked to the physical screen edge (OuterBounds, not WorkArea) at the chosen position.</summary>
    private RectInt32 ComputeRect(int width, int height)
    {
        // The monitor's full rectangle, not its work area: a notch hugs the physical screen edge even when another
        // app-bar (or the taskbar at the top) reserves space there.
        var r = NotchPlacement.Dock(Bounds(), width, height, _position, _scale);
        return new RectInt32(r.X, r.Y, r.Width, r.Height);
    }

    private void ApplyFrame()
    {
        var width = Math.Max(1, (int)Math.Round(_curW));
        var height = Math.Max(1, (int)Math.Round(_curH));

        var rect = ComputeRect(width, height);
        if (rect.X == _lastRect.X && rect.Y == _lastRect.Y && rect.Width == _lastRect.Width && rect.Height == _lastRect.Height)
        {
            return; // nothing moved: skip the move and the region reset, which each force a repaint
        }

        _lastRect = rect;
        _appWindow.MoveAndResize(rect);

        Win32Window.ApplyNotchShape(_hwnd, width, height, RadiusPx(height), ShoulderPx());
    }
}
