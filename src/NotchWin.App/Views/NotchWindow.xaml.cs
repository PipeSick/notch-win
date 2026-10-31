using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using NotchWin.App.Modules.Notch;
using NotchWin.App.ViewModels;
using NotchWin.Core.Models;
using NotchWin.Core.Music;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace NotchWin.App.Views;

public sealed partial class NotchWindow : Window
{
    private const int ExpandedWidth = 440;
    private const int CollapsedHeight = 32;
    private const int CollapsedIdleWidth = 180;
    private const int CollapsedTrackWidth = 300;
    private const int ToastWidth = 340;
    private const int ToastMs = 3200;

    private static readonly Random Rng = new();

    private readonly NotchViewModel _viewModel;
    private readonly DispatcherQueueTimer _uiTimer;
    private readonly DispatcherQueueTimer _hoverTimer;
    private readonly Rectangle[] _eqBars;

    private readonly GlassBackdrop _glass;
    private readonly DispatcherQueueTimer _glassSaveTimer;
    private NotchWindowController? _controller;
    private NowPlayingInfo? _info;
    private DateTime _infoAtUtc;
    private bool _expanded;
    private bool _hasTrack;
    private bool _toasting;
    private string? _lastTitle;
    private string? _lastArtist;
    private readonly DispatcherQueueTimer _toastTimer;

    public NotchWindow(NotchViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        _eqBars = [Eq0, Eq1, Eq2, Eq3];

        _glass = new GlassBackdrop { Level = _viewModel.GlassLevel / 100.0 };
        SystemBackdrop = _glass;
        GlassSlider.Value = _viewModel.GlassLevel;
        _glassSaveTimer = DispatcherQueue.CreateTimer();
        _glassSaveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _glassSaveTimer.IsRepeating = false;
        _glassSaveTimer.Tick += (_, _) => _viewModel.PersistGlass?.Invoke();
        // Hook the slider up only once it has loaded: its first layout can report a spurious value
        // (the minimum), which would otherwise overwrite the saved glass level.
        var glassHooked = false;
        GlassSlider.Loaded += (_, _) =>
        {
            if (glassHooked)
            {
                return;
            }

            glassHooked = true;
            GlassSlider.Value = _viewModel.GlassLevel;
            GlassSlider.ValueChanged += (_, e) =>
            {
                _viewModel.GlassLevel = (int)Math.Round(e.NewValue);
                _glassSaveTimer.Stop();
                _glassSaveTimer.Start();
            };
        };
        _viewModel.GlassChanged += () =>
        {
            _glass.Level = _viewModel.GlassLevel / 100.0;
            GlassSlider.Value = _viewModel.GlassLevel;
        };

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.AgentSnapshots.CollectionChanged += OnAgentsChanged;

        _uiTimer = DispatcherQueue.CreateTimer();
        _uiTimer.Interval = TimeSpan.FromMilliseconds(130);
        _uiTimer.Tick += (_, _) => OnUiTick();
        _uiTimer.Start();

        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(ToastMs);
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick += (_, _) => EndToast();

        _hoverTimer = DispatcherQueue.CreateTimer();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(1100);
        _hoverTimer.IsRepeating = false;
        _hoverTimer.Tick += (_, _) =>
        {
            if (_expanded)
            {
                SetExpanded(false);
            }
        };

        RebuildAgents();
        InitSections();
        _ = UpdateNowPlayingUiAsync(_viewModel.NowPlaying);
    }

    /// <summary>
    /// Set once by the composition root right after construction — the controller needs this
    /// window's HWND, which only exists once the base <see cref="Window"/> constructor has run.
    /// </summary>
    public void AttachController(NotchWindowController controller)
    {
        _controller = controller;
        ApplyLayout(animate: false);
    }

    private void OnBarTapped(object sender, TappedRoutedEventArgs e) => SetExpanded(!_expanded);

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => _hoverTimer.Stop();

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_expanded)
        {
            _hoverTimer.Start();
        }
    }

    private void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        if (expanded)
        {
            _toastTimer.Stop();
            _toasting = false;
            ToastBar.Visibility = Visibility.Collapsed;
            CollapsedBar.Visibility = Visibility.Collapsed;
            ExpandedPanel.Visibility = Visibility.Visible;
            _customizing = false;
            ShowActivePane();
            RetryLyricsIfNeeded();
            ApplyLayout(animate: true);
        }
        else
        {
            _hoverTimer.Stop();
            UpdateCamera(); // release the webcam the moment the notch closes
            ApplyLayout(animate: true, onCompleted: () =>
            {
                if (!_expanded)
                {
                    ExpandedPanel.Visibility = Visibility.Collapsed;
                    CollapsedBar.Visibility = Visibility.Visible;
                }
            });
        }
    }

    /// <summary>Works out the size the window should have for its current content and moves the controller there.</summary>
    private void ApplyLayout(bool animate, Action? onCompleted = null)
    {
        if (_controller is null)
        {
            return;
        }

        int width, height;
        if (_expanded)
        {
            ExpandedPanel.Measure(new Size(ExpandedWidth, double.PositiveInfinity));
            width = ExpandedWidth;
            height = (int)Math.Ceiling(ExpandedPanel.DesiredSize.Height);
        }
        else
        {
            width = _toasting ? ToastWidth : _hasTrack || _viewModel.Timer.IsRunning ? CollapsedTrackWidth : CollapsedIdleWidth;
            height = CollapsedHeight;
        }

        if (animate)
        {
            _controller.AnimateTo(width, height, onCompleted);
        }
        else
        {
            _controller.SnapTo(width, height);
            onCompleted?.Invoke();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotchViewModel.NowPlaying))
        {
            _ = UpdateNowPlayingUiAsync(_viewModel.NowPlaying);
        }
    }

    private void OnAgentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Replace-all arrives as Clear + N Adds; rebuild once per burst on the next dispatcher turn.
        if (_agentsRebuildQueued)
        {
            return;
        }

        _agentsRebuildQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _agentsRebuildQueued = false;
            RebuildAgents();
            if (_expanded)
            {
                ApplyLayout(animate: true);
            }
        });
    }

    private bool _agentsRebuildQueued;

    private async Task UpdateNowPlayingUiAsync(NowPlayingInfo? info)
    {
        _info = info;
        _infoAtUtc = DateTime.UtcNow;

        var hasTrack = info is not null && !string.IsNullOrEmpty(info.Title);
        _hasTrack = hasTrack;
        MiniTitle.Text = hasTrack ? info!.Title : string.Empty;

        // GSMTC re-raises Changed for position/playback updates too, so only a new title/artist is a track change.
        var trackChanged = hasTrack && (info!.Title != _lastTitle || info.Artist != _lastArtist);
        _lastTitle = hasTrack ? info!.Title : null;
        _lastArtist = hasTrack ? info!.Artist : null;

        if (trackChanged)
        {
            _ = LoadLyricsAsync(info!);
        }
        else if (!hasTrack)
        {
            _lyrics = null;
            LyricsBox.Visibility = Visibility.Collapsed;
        }
        TitleText.Text = hasTrack ? info!.Title : "Nada en reproducción";
        ArtistText.Text = hasTrack ? info!.Artist : string.Empty;
        _sourceName = hasTrack ? FriendlySource(info!.SourceAppName) : null;
        UpdateHeader();
        ProgressRow.Visibility = hasTrack && info!.Duration is not null ? Visibility.Visible : Visibility.Collapsed;
        ControlsRow.Visibility = hasTrack && _viewModel.MediaControls is not null ? Visibility.Visible : Visibility.Collapsed;
        PlayPauseIcon.Glyph = info is { IsPlaying: true } ? "" : "";

        var playing = info is { IsPlaying: true };
        MiniArt.Visibility = hasTrack ? Visibility.Visible : Visibility.Collapsed;
        EqPanel.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress();

        ImageSource? art = null;
        if (info?.AlbumArt is { Length: > 0 } bytes)
        {
            art = await ToBitmapImageAsync(bytes);
        }

        SetArt(ArtBorder, art, ArtPlaceholder);
        SetArt(MiniArt, art, null);
        SetArt(ToastArt, art, null);

        if (trackChanged && !_expanded)
        {
            ShowTrackToast(info!);
        }
        else
        {
            ApplyLayout(animate: true);
        }
    }

    private void ShowTrackToast(NowPlayingInfo info) => ShowToast(info.Title, info.Artist);

    /// <summary>Widens the collapsed pill for a few seconds; <paramref name="art"/> keeps the current cover when null.</summary>
    private void ShowToast(string title, string subtitle, Brush? art = null)
    {
        ToastTitle.Text = title;
        ToastArtist.Text = subtitle;
        if (art is not null)
        {
            ToastArt.Background = art;
        }

        _toasting = true;
        CollapsedBar.Visibility = Visibility.Collapsed;
        ToastBar.Visibility = Visibility.Visible;
        ApplyLayout(animate: true);

        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void EndToast()
    {
        _toastTimer.Stop();
        if (!_toasting)
        {
            return;
        }

        _toasting = false;
        ToastBar.Visibility = Visibility.Collapsed;
        if (!_expanded)
        {
            CollapsedBar.Visibility = Visibility.Visible;
            ApplyLayout(animate: true);
        }
    }

    private static void SetArt(Border target, ImageSource? source, FontIcon? placeholder)
    {
        target.Background = source is null
            ? new SolidColorBrush(Color.FromArgb(255, 0x1C, 0x1C, 0x1E))
            : new ImageBrush { ImageSource = source, Stretch = Stretch.UniformToFill };
        if (placeholder is not null)
        {
            placeholder.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnUiTick()
    {
        if (_info is { IsPlaying: true })
        {
            foreach (var bar in _eqBars)
            {
                bar.Height = 4 + Rng.Next(0, 12);
            }

            if (_expanded)
            {
                UpdateProgress();
                if (_active == Core.Settings.NotchSections.Music)
                {
                    UpdateLyrics();
                }
            }
        }
    }

    private void UpdateProgress()
    {
        if (_info is not { Duration: { } duration } || duration <= TimeSpan.Zero)
        {
            return;
        }

        var position = _info.Position + (_info.IsPlaying ? DateTime.UtcNow - _infoAtUtc : TimeSpan.Zero);
        position = position < TimeSpan.Zero ? TimeSpan.Zero : position > duration ? duration : position;

        var fraction = Math.Clamp(position / duration, 0.0, 1.0);
        ProgressDone.Width = new GridLength(fraction, GridUnitType.Star);
        ProgressRest.Width = new GridLength(1 - fraction, GridUnitType.Star);
        ElapsedText.Text = FormatTime(position);
        DurationText.Text = FormatTime(duration);
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    private static string FriendlySource(string appId)
    {
        var name = appId;
        var cut = name.LastIndexOfAny(['!', '\\', '/']);
        if (cut >= 0)
        {
            name = name[(cut + 1)..];
        }

        name = name.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase);
        return name.Length == 0 ? "NotchWin" : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private async void OnPlayPauseClicked(object sender, RoutedEventArgs e) => await RunControl(c => c.TogglePlayPauseAsync());

    private async void OnNextClicked(object sender, RoutedEventArgs e) => await RunControl(c => c.NextAsync());

    private async void OnPreviousClicked(object sender, RoutedEventArgs e) => await RunControl(c => c.PreviousAsync());

    private async Task RunControl(Func<Modules.Music.IMediaControls, Task> action)
    {
        try
        {
            if (_viewModel.MediaControls is { } controls)
            {
                await action(controls);
            }
        }
        catch (Exception)
        {
            // The owning app can reject or vanish mid-command; a failed transport button isn't worth a crash.
        }
    }

    private void RebuildAgents()
    {
        AgentsList.Children.Clear();
        var visible = _viewModel.AgentSnapshots.Where(s => s.Error is null).ToList();
        AgentsEmpty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var snapshot in visible)
        {
            AgentsList.Children.Add(BuildAgentRow(snapshot));
        }
    }

    private static UIElement BuildAgentRow(AgentUsageSnapshot s)
    {
        var accent = AccentFor(s.Kind);
        var row = new StackPanel { Spacing = 6 };

        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(accent), VerticalAlignment = VerticalAlignment.Center,
            Opacity = s.IsLiveWorking ? 1 : 0.55,
        };
        head.Children.Add(dot);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock
        {
            Text = NameFor(s), Foreground = new SolidColorBrush(Colors.White), FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        var subtitle = s.IsLiveWorking ? "Trabajando…" : s.ActiveProject ?? (s.Models.Count > 0 ? s.Models[0] : null);
        if (!string.IsNullOrEmpty(subtitle))
        {
            names.Children.Add(new TextBlock
            {
                Text = subtitle, Foreground = new SolidColorBrush(Color.FromArgb(255, 0x8E, 0x8E, 0x93)), FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1,
            });
        }

        Grid.SetColumn(names, 1);
        head.Children.Add(names);

        var totals = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        totals.Children.Add(new TextBlock
        {
            Text = FormatTokens(s.TokensToday.Total) + " tok", Foreground = new SolidColorBrush(Colors.White),
            FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right,
        });
        if (s.EstimatedCostUsdToday is { } cost)
        {
            totals.Children.Add(new TextBlock
            {
                Text = (s.IsEstimate ? "~" : string.Empty) + "$" + cost.ToString("0.00"),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0x8E, 0x8E, 0x93)), FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
            });
        }

        Grid.SetColumn(totals, 2);
        head.Children.Add(totals);
        row.Children.Add(head);

        foreach (var limit in s.PlanLimits.Take(2))
        {
            row.Children.Add(BuildLimitBar(limit, accent));
        }

        return row;
    }

    private static UIElement BuildLimitBar(PlanLimitInfo limit, Color accent)
    {
        var fraction = Math.Clamp(limit.UsedPercent / 100.0, 0, 1);
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

        grid.Children.Add(new TextBlock
        {
            Text = limit.Label, FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 0x8E, 0x8E, 0x93)),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var bar = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
        var track = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 0x3A, 0x3A, 0x3C)), CornerRadius = new CornerRadius(2) };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { Background = new SolidColorBrush(fraction > 0.85 ? Color.FromArgb(255, 0xFF, 0x45, 0x3A) : accent), CornerRadius = new CornerRadius(2) });
        Grid.SetColumn(bar, 1);
        grid.Children.Add(bar);

        var pct = new TextBlock
        {
            Text = $"{limit.UsedPercent:0}%", FontSize = 10, Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(pct, 2);
        grid.Children.Add(pct);
        return grid;
    }

    private static string NameFor(AgentUsageSnapshot s)
    {
        var baseName = s.Kind switch
        {
            AgentKind.ClaudeCode => "Claude Code",
            AgentKind.Codex => "Codex",
            AgentKind.Copilot => "Copilot",
            AgentKind.OpenCode => "OpenCode",
            _ => s.Kind.ToString(),
        };
        return s.AccountLabel is { Length: > 0 } label && label != "default" ? $"{baseName} · {label}" : baseName;
    }

    private static Color AccentFor(AgentKind kind) => kind switch
    {
        AgentKind.ClaudeCode => Color.FromArgb(255, 0xD9, 0x77, 0x57),
        AgentKind.Codex => Color.FromArgb(255, 0x10, 0xA3, 0x7F),
        AgentKind.OpenCode => Color.FromArgb(255, 0x3B, 0x9C, 0xFF),
        _ => Color.FromArgb(255, 0x8B, 0x5C, 0xF6),
    };

    private static string FormatTokens(long n) => n switch
    {
        >= 1_000_000 => (n / 1_000_000.0).ToString("0.0") + "M",
        >= 1_000 => (n / 1_000.0).ToString("0.0") + "K",
        _ => n.ToString(),
    };

    private static async Task<BitmapImage> ToBitmapImageAsync(byte[] data)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(data.AsBuffer());
        stream.Seek(0);

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
