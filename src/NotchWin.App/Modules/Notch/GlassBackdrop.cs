using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace NotchWin.App.Modules.Notch;

/// <summary>
/// Dark acrylic whose tint is adjustable. The stock acrylic backdrop turns into a flat fallback color
/// whenever its window isn't active, and the notch never activates, so this keeps input "active".
/// </summary>
internal sealed class GlassBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;
    private double _level = 0.6;
    private bool _matchBar;

    /// <summary>
    /// Inside WinMac, collapsed: the same tint and opacity as WinMac's top bar in dark mode (BarPainter: rgb 0.12/0.13/0.16 at 38 % over the blurred wallpaper), so the notch
    /// reads as part of the bar. Open, it goes back to the darker glass its content needs.
    /// </summary>
    public bool MatchBar
    {
        get => _matchBar;
        set
        {
            if (_matchBar == value) return;
            _matchBar = value;
            Apply();
        }
    }

    /// <summary>0 = mostly clear glass, 1 = solid black.</summary>
    public double Level
    {
        get => _level;
        set
        {
            _level = Math.Clamp(value, 0, 1);
            Apply();
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _configuration = new SystemBackdropConfiguration { IsInputActive = true, Theme = SystemBackdropTheme.Dark };
        _controller = new DesktopAcrylicController();
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_configuration);
        Apply();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
    }

    private void Apply()
    {
        if (_controller is null)
        {
            return;
        }

        if (_matchBar)
        {
            var bar = Microsoft.UI.ColorHelper.FromArgb(255, 31, 33, 41);   // WinMac BarPainter, dark theme
            _controller.TintColor = bar;
            _controller.FallbackColor = Microsoft.UI.ColorHelper.FromArgb(255, 38, 40, 48);
            _controller.TintOpacity = 0.38f;
            _controller.LuminosityOpacity = 0.12f;
            return;
        }

        // Liquid Glass approximation: a neutral graphite tint (not black) that lets the live blur of what is behind show through. The slider follows a curve: the default
        // (0.6) is clearly glass (tint ≈ 38 %), the top end still reaches an almost solid surface. Before, even the lowest setting was 45 % black plus a 30 % black scrim.
        var t = _level * _level;
        _controller.TintColor = Microsoft.UI.ColorHelper.FromArgb(255, 24, 24, 28);
        _controller.FallbackColor = Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 34);
        _controller.TintOpacity = (float)(0.08 + 0.82 * t);
        _controller.LuminosityOpacity = (float)(0.12 + 0.78 * t);
    }
}
