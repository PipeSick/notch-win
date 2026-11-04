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

    private bool _light;
    /// <summary>It7: Windows is in light theme: the glass is LIGHT (graphite text) and its tint keeps a minimum opacity so the text stays readable over anything behind (GlassTint, IslandThemeTests).</summary>
    public bool LightSurroundings
    {
        get => _light;
        set { if (_light == value) return; _light = value; Apply(); }
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
        _configuration = new SystemBackdropConfiguration { IsInputActive = true, Theme = _light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark };
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

        var (tint, lum) = NotchWin.Core.Notch.GlassTint.For(_level, _matchBar, _light);
        // Dark: WinMac BarPainter's tint when collapsed inside WinMac, otherwise the Liquid Glass graphite. Light: the equivalent light glass (IslandPalette).
        var baseTint = NotchWin.Core.Notch.IslandPalette.GlassBase(_light, _matchBar); var fallback = NotchWin.Core.Notch.IslandPalette.GlassFallback(_light, _matchBar);
        _controller.TintColor = Microsoft.UI.ColorHelper.FromArgb(255, baseTint.R, baseTint.G, baseTint.B);
        _controller.FallbackColor = Microsoft.UI.ColorHelper.FromArgb(255, fallback.R, fallback.G, fallback.B);
        if (_configuration is not null) _configuration.Theme = _light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;

        _controller.TintOpacity = tint;
        _controller.LuminosityOpacity = lum;
    }
}
