using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private const int CallWidth = 440;   // docs/60: room for the call buttons

    private PillArbiter _pill = null!;
    private DispatcherQueueTimer _pillTimer = null!;
    private PillItem? _shownPill;
    private DispatcherQueueTimer? _callClock;   // docs/60: the active call's duration

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
            if (item.Withdraw) _pill.Withdraw(item.Kind); else _pill.Post(item);
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
        PillKind.Call when _shownPill.Controls?.Any == true => (CallWidth, Math.Max(NotifHeight, CollapsedHeight)),
        PillKind.Notification or PillKind.Call => (NotifWidth, Math.Max(NotifHeight, CollapsedHeight)),
        _ => (_toastWidth, CollapsedHeight),
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
                case PillKind.Notification or PillKind.Call: ShowNotif(item); break;   // WM-038: the call uses the notification's layout (no new design)
                default: ShowBanner(item); break;
            }
        }

        // An indicator that only changes its level keeps its size: no relayout, so holding the volume key never makes the pill pulse.
        bool sameShape = previous is not null && item is not null && previous.Kind == item.Kind && item.Kind is PillKind.Volume or PillKind.Brightness;
        if (!sameShape)
        {
            ApplyLayout(animate: true);
        }
        SelfTestLog(item);
    }

    /// <summary>CI only: with WINMAC_PILL_SELFTEST=&lt;file&gt; every change of the pill is written there (kind, level, title and the size asked of the window), so the
    /// Windows runner can check the whole path bar → host → notch → screen. Without the variable nothing is written.</summary>
    private static readonly string? SelfTestPath = Environment.GetEnvironmentVariable("WINMAC_PILL_SELFTEST");

    private void SelfTestLog(PillItem? item)
    {
        if (string.IsNullOrEmpty(SelfTestPath))
        {
            return;
        }

        try
        {
            var (w, h) = PillSize();
            var line = item is null ? "pill none" : $"pill {item.Kind} level={item.Level} muted={item.Muted} icon={(item.Icon is null ? "no" : "yes")} title={item.Title} size={w}x{h}";
            File.AppendAllText(SelfTestPath, $"{DateTime.UtcNow:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // test aid only
        }
    }

    private void ScheduleTick()
    {
        _pillTimer.Stop();
        if (_pill.NextWait() is { } wait)
        {
            _pillTimer.Interval = wait;
            _pillTimer.Start();
        }
    }

    /// <summary>The pill timer fired: advance, then ALWAYS schedule again (a timer that fires a few ms early would otherwise leave the pill stuck).</summary>
    private void OnPillTimer()
    {
        _pill.Tick();
        ScheduleTick();
    }

    /// <summary>Watchdog run by the UI clock: if an item is past its deadline (a lost or late timer), move on now.</summary>
    private void PillWatchdog()
    {
        if (_pill?.Overdue == true)
        {
            OnPillTimer();
        }
    }

    private void HidePillBars()
    {
        _callClock?.Stop();
        NotifBar.IsHitTestVisible = false;
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
        var dim = new SolidColorBrush(Pal.C(255, 0x8E, 0x8E, 0x93));
        HudIcon.Foreground = muted ? dim : new SolidColorBrush(Pal.Fg);
        HudFill.Background = muted ? dim : new SolidColorBrush(Pal.Fg);
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
        ShowCallButtons(item);
        NotifBar.Visibility = Visibility.Visible;
    }

    // ── Docs/60: call buttons and duration ──
    private void ShowCallButtons(PillItem item)
    {
        var c = item.Controls ?? default;
        CallAnswerButton.Visibility = c.Answer ? Visibility.Visible : Visibility.Collapsed;
        CallRejectButton.Visibility = c.Reject ? Visibility.Visible : Visibility.Collapsed;
        CallHangupButton.Visibility = c.Hangup ? Visibility.Visible : Visibility.Collapsed;
        CallMuteButton.Visibility = c.Mute ? Visibility.Visible : Visibility.Collapsed;
        CallRouteButton.Visibility = c.Route ? Visibility.Visible : Visibility.Collapsed;
        CallButtons.Visibility = c.Any ? Visibility.Visible : Visibility.Collapsed;
        NotifBar.IsHitTestVisible = c.Any;   // a plain notification stays click-through; only a call with working buttons takes clicks
        if (item.IsActiveCall && !item.HasNotice)
        {
            _callClock ??= CreateCallClock();
            _callClock.Start();
        }
        else
        {
            _callClock?.Stop();
        }
    }

    private DispatcherQueueTimer CreateCallClock()
    {
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(1);
        t.IsRepeating = true;
        t.Tick += (_, _) =>
        {
            if (_shownPill is { IsActiveCall: true, HasNotice: false } p)
            {
                NotifBody.Text = PillFeed.ActiveSubtitle(p.CallSinceMs, p.AudioOnPc, p.CallMuted, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
        };
        return t;
    }

    private void OnCallAnswer(object sender, RoutedEventArgs e) => SendCall("answer");
    private void OnCallReject(object sender, RoutedEventArgs e) => SendCall("reject");
    private void OnCallHangup(object sender, RoutedEventArgs e) => SendCall("hangup");
    private void OnCallMute(object sender, RoutedEventArgs e) => SendCall("mute");
    private void OnCallRoute(object sender, RoutedEventArgs e) => SendCall("route");

    private static void SendCall(string cmd)
    {
        try
        {
            Program.Hosted?.SendCallCommand(cmd);   // Hatsu Connect checks that Windows can really do it at that moment
        }
        catch (Exception)
        {
            // the link may be down: nothing to do, the buttons come back with the next call state
        }
    }

    private readonly TextBlock _toastTitleMeasurer = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock _toastSubMeasurer = new() { FontSize = 12 };
    private int _toastWidth = ToastWidth;

    /// <summary>Padding (14 + 14), cover (20), spacing (8 + 8) and a little air: what the banner needs around its two lines of text.</summary>
    private const int ToastChrome = 14 + 14 + 20 + 8 + 8 + 12;

    /// <summary>The banner is as wide as its text needs (title + detail), up to 40% of the monitor (never above 720): «Sin confirmar 291470 · 263,9 MB · 2,0 MB/s · Chrome» used to be cut at fixed 170 + 100 px.</summary>
    private void FitBannerWidth()
    {
        _toastTitleMeasurer.Text = ToastTitle.Text;
        _toastSubMeasurer.Text = ToastArtist.Text;
        _toastTitleMeasurer.Measure(new Windows.Foundation.Size(double.PositiveInfinity, 32));
        _toastSubMeasurer.Measure(new Windows.Foundation.Size(double.PositiveInfinity, 32));
        var titleW = (int)Math.Ceiling(_toastTitleMeasurer.DesiredSize.Width);
        var subW = string.IsNullOrEmpty(ToastArtist.Text) ? 0 : (int)Math.Ceiling(_toastSubMeasurer.DesiredSize.Width);
        var cap = Math.Clamp((int)((_controller?.MonitorWidthDip ?? 1920) * 0.4), ToastWidth, MaxLyricPillWidth);
        var wanted = ToastChrome + titleW + subW + (subW > 0 ? 8 : 0);
        _toastWidth = Math.Clamp((wanted + 19) / 20 * 20, ToastWidth, cap);   // rounded up so tiny differences do not make the notch twitch
        var room = _toastWidth - ToastChrome;
        ToastTitle.MaxWidth = subW > 0 ? Math.Max(120, Math.Min(titleW, room - Math.Min(subW, room / 2))) : room;
        ToastArtist.MaxWidth = Math.Max(60, room - Math.Min(titleW, ToastTitle.MaxWidth) - 8);
    }

    private void ShowBanner(PillItem item)
    {
        HudBar.Visibility = Visibility.Collapsed;
        NotifBar.Visibility = Visibility.Collapsed;
        ToastTitle.Text = item.Title;
        ToastArtist.Text = item.Subtitle ?? "";
        FitBannerWidth();
        ToastArt.Background = item.Kind == PillKind.Activity
            ? new SolidColorBrush(ColorForSource(item.App ?? ""))
            : item.Kind == PillKind.Capture && item.ImagePath is { } img ? Thumbnail(img, 48)   // the saved screenshot, decoded small
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
