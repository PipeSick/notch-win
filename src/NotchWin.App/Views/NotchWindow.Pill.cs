using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NotchWin.Core.Pill;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>
/// The collapsed notch's transient content: volume and brightness indicators, Windows notifications relayed by WinMac, NotchWin's own events and the new-track banner.
/// What is shown, for how long and what interrupts what is decided by one <see cref="PillArbiter"/> per window (built from the settings in the view model);
/// this file only draws its <see cref="PillArbiter.Current"/>. With nothing current the usual pill (track or idle) is back.
/// </summary>
public sealed partial class NotchWindow
{
    private const int HudWidth = 280;
    private const int NotifWidth = 380;
    private const int NotifHeight = 52;

    private PillArbiter _pill = null!;
    private DispatcherQueueTimer _pillTimer = null!;
    private PillItem? _shownPill;

    private void InitPill()
    {
        _pill = new PillArbiter(_viewModel.PillPolicy, () => DateTime.UtcNow);
        _pill.Changed += OnPillChanged;
        _viewModel.PillPosted += OnPillPosted;
        _viewModel.PillPolicyChanged += OnPillPolicyChanged;
        HudTrack.SizeChanged += (_, _) => UpdateHudFill();
    }

    private void DetachPill()
    {
        _viewModel.PillPosted -= OnPillPosted;
        _viewModel.PillPolicyChanged -= OnPillPolicyChanged;
        _pill.Changed -= OnPillChanged;
        _pillTimer.Stop();
    }

    private void OnPillPosted(PillItem item)
    {
        if (!_closed)
        {
            _pill.Post(item);
        }
    }

    private void OnPillPolicyChanged()
    {
        if (!_closed)
        {
            _pill.SetPolicy(_viewModel.PillPolicy);
        }
    }

    /// <summary>Size of the collapsed window for the transient item on screen; (0, 0) when the usual pill is showing.</summary>
    private (int Width, int Height) PillSize() => _expanded || _shownPill is null ? (0, 0) : _shownPill.Kind switch
    {
        PillKind.Volume or PillKind.Brightness => (HudWidth, CollapsedHeight),
        PillKind.Notification => (NotifWidth, Math.Max(NotifHeight, CollapsedHeight)),
        _ => (ToastWidth, CollapsedHeight),
    };

    private void OnPillChanged()
    {
        if (_closed)
        {
            return;
        }

        var item = _pill.Current;
        var previous = _shownPill;
        _shownPill = _expanded ? null : item;
        ScheduleTick();
        if (_expanded)
        {
            return;
        }

        if (item is null)
        {
            HidePillBars();
            CollapsedBar.Visibility = Visibility.Visible;
        }
        else
        {
            CollapsedBar.Visibility = Visibility.Collapsed;
            switch (item.Kind)
            {
                case PillKind.Volume or PillKind.Brightness: ShowHud(item); break;
                case PillKind.Notification: ShowNotif(item); break;
                default: ShowBanner(item); break;
            }
        }

        // An indicator that only changes its level keeps its size: no relayout, so holding the volume key never makes the pill pulse.
        bool sameShape = previous is not null && item is not null && previous.Kind == item.Kind && item.Kind is PillKind.Volume or PillKind.Brightness;
        if (!sameShape)
        {
            ApplyLayout(animate: true);
        }
    }

    private void ScheduleTick()
    {
        _pillTimer.Stop();
        if (_pill.NextDeadline is { } at)
        {
            var wait = at - DateTime.UtcNow;
            _pillTimer.Interval = wait > TimeSpan.FromMilliseconds(15) ? wait : TimeSpan.FromMilliseconds(15);
            _pillTimer.Start();
        }
    }

    private void HidePillBars()
    {
        ToastBar.Visibility = Visibility.Collapsed;
        HudBar.Visibility = Visibility.Collapsed;
        NotifBar.Visibility = Visibility.Collapsed;
    }

    private double _hudLevel;

    private void ShowHud(PillItem item)
    {
        ToastBar.Visibility = Visibility.Collapsed;
        NotifBar.Visibility = Visibility.Collapsed;
        HudIcon.Glyph = item.Glyph;
        HudText.Text = item.LevelText;
        bool muted = item.Kind == PillKind.Volume && item.Muted;
        var dim = new SolidColorBrush(Color.FromArgb(255, 0x8E, 0x8E, 0x93));
        HudIcon.Foreground = muted ? dim : new SolidColorBrush(Microsoft.UI.Colors.White);
        HudFill.Background = muted ? dim : new SolidColorBrush(Microsoft.UI.Colors.White);
        _hudLevel = item.Level / 100.0;
        HudBar.Visibility = Visibility.Visible;
        UpdateHudFill();
    }

    private void UpdateHudFill()
    {
        var w = HudTrack.ActualWidth;
        HudFill.Width = w > 0 ? Math.Round(w * Math.Clamp(_hudLevel, 0, 1)) : 0;
    }

    private void ShowNotif(PillItem item)
    {
        ToastBar.Visibility = Visibility.Collapsed;
        HudBar.Visibility = Visibility.Collapsed;
        NotifTitle.Text = item.Title;
        NotifBody.Text = item.Subtitle ?? "";
        NotifBody.Visibility = string.IsNullOrEmpty(item.Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        var icon = item.Icon is { } px ? ToBitmap(px, item.IconWidth, item.IconHeight) : null;
        NotifIcon.Source = icon;
        NotifIcon.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        NotifInitialBack.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        var name = item.App ?? item.Title;
        NotifInitial.Text = string.IsNullOrWhiteSpace(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString();
        NotifBar.Height = Math.Max(NotifHeight, CollapsedHeight);
        NotifBar.Visibility = Visibility.Visible;
    }

    private void ShowBanner(PillItem item)
    {
        HudBar.Visibility = Visibility.Collapsed;
        NotifBar.Visibility = Visibility.Collapsed;
        ToastTitle.Text = item.Title;
        ToastArtist.Text = item.Subtitle ?? "";
        ToastArt.Background = item.Kind == PillKind.Activity
            ? new SolidColorBrush(ColorForSource(item.App ?? ""))
            : MiniArt.Background;   // a new track shows its cover (the mini cover already holds it)
        ToastBar.Visibility = Visibility.Visible;
    }

    /// <summary>The app icon WinMac sent: 32-bit BGRA premultiplied, which is exactly a WriteableBitmap's buffer. Null if it cannot be built.</summary>
    private static WriteableBitmap? ToBitmap(byte[] px, int w, int h)
    {
        try
        {
            if (w <= 0 || h <= 0 || px.Length != w * h * 4)
            {
                return null;
            }

            var bmp = new WriteableBitmap(w, h);
            using (var stream = bmp.PixelBuffer.AsStream())
            {
                stream.Write(px, 0, px.Length);
            }

            bmp.Invalidate();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
