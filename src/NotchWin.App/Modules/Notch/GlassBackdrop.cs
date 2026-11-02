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

        _controller.TintColor = Colors.Black;
        _controller.FallbackColor = Colors.Black;
        // Never fully clear: at the low end the blurred wallpaper (or a white window) washes out the white
        // icons and text, so the glass keeps a dark floor and the slider only moves it from there.
        _controller.TintOpacity = (float)(0.45 + 0.55 * _level);
        _controller.LuminosityOpacity = (float)(0.4 + 0.6 * _level);
    }
}
