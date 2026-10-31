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

        _controller.TintColor = Colors.Black;
        _controller.FallbackColor = Colors.Black;
        // Never fully clear: at the low end the blurred wallpaper (or a white window) washes out the white
        // icons and text, so the glass keeps a dark floor and the slider only moves it from there.
        _controller.TintOpacity = (float)(0.45 + 0.55 * _level);
        _controller.LuminosityOpacity = (float)(0.4 + 0.6 * _level);
    }
}
