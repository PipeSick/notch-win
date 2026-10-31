using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NotchWin.App.Modules.Clipboard;
using NotchWin.App.Modules.Music;
using NotchWin.App.Modules.Notch;
using NotchWin.App.Services;
using NotchWin.App.ViewModels;
using NotchWin.App.Views;
using NotchWin.Core.AiAgents;
using NotchWin.Core.AiAgents.ClaudeCode;
using NotchWin.Core.AiAgents.Codex;
using NotchWin.Core.AiAgents.Copilot;
using NotchWin.Core.AiAgents.OpenCode;
using NotchWin.Core.Lyrics;
using NotchWin.Core.Calendar;
using NotchWin.Core.Downloads;
using NotchWin.Core.Models;
using NotchWin.Core.Music;
using NotchWin.Core.Pricing;
using NotchWin.Core.Hosting;
using NotchWin.Core.Settings;
using NotchWin.Core.Usage;

namespace NotchWin.App;

public partial class App : Application
{
    private readonly SettingsStore _settingsStore = new(SettingsStore.DefaultPath());
    private AppSettings _settings = new();

    // One notch per connected monitor, keyed by the monitor's display id and kept in step by _displayWatcher.
    private readonly Dictionary<ulong, (NotchWindow Window, NotchWindowController Controller)> _notches = [];
    private DisplayAreaWatcher? _displayWatcher;
    private DispatcherQueue? _uiQueue;    private NotchViewModel? _viewModel;
    private GsmtcMediaProvider? _mediaProvider;
    private ClipboardWatcher? _clipboardWatcher;
    private AgentPollingService? _agentPolling;
    private TrayIconService? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private readonly AgentCompletionDetector _completion = new();
    private DownloadTracker? _downloads;
    private DispatcherQueueTimer? _clock;
    private int _clockTicks;

    // Hosted mode (started by WinMac): the host's settings are the single source of truth; this process never writes the central fields to disk.
    private HostedSession? _hosted;
    private CentralSettings? _central;
    private CentralSettings? _firstCentral;
    private readonly ManualResetEventSlim _firstCentralArrived = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _uiQueue = DispatcherQueue.GetForCurrentThread();
        _settingsStore.RemoveLegacySecrets(); // an old plaintext Anthropic key must not stay on disk
        _settings = _settingsStore.Load();
        StartHostedLink();

        _viewModel = new NotchViewModel
        {
            Sections = NotchSections.Normalize(_settings.NotchSections),
            PersistSections = PersistSections,
            GlassLevel = _settings.GlassLevel,
            PersistGlass = PersistGlass,
            PlayerName = MusicPlayerLauncher.DisplayName(_settings.MusicPlayer, _settings.CustomPlayerPath),
            OpenPlayer = OpenPlayer,
            History = new UsageHistory(Path.Combine(Path.GetDirectoryName(_settingsStore.FilePath) ?? ".", "usage-history.json")),
        };
        // One notch per monitor, all sharing the same view model.
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _uiQueue = dispatcherQueue;
        var displays = DisplayArea.FindAll();
        for (var i = 0; i < displays.Count; i++)
        {
            AddNotch(displays[i]);
        }

        WatchDisplays();

        StartClock(dispatcherQueue);
        ApplyClipboardSetting();

        if (_hosted is null)
        {
            // Standalone: NotchWin has its own tray icon. Inside WinMac the tray belongs to WinMac, so there is none here.
            _trayIcon = new TrayIconService("NotchWin");
            _trayIcon.SettingsRequested += () => dispatcherQueue.TryEnqueue(OpenSettingsWindow);
            _trayIcon.ExitRequested += () => dispatcherQueue.TryEnqueue(ExitApp);
        }

        if (_settings.MusicModuleEnabled)
        {
            StartMusicModule(dispatcherQueue);
        }

        if (_settings.AiAgentsModuleEnabled)
        {
            StartAgentPolling(dispatcherQueue);
        }
    }

    // ---------- one notch per monitor, following monitors as they come and go ----------

    private void AddNotch(DisplayArea area)
    {
        var id = area.DisplayId.Value;
        if (_viewModel is null || _notches.ContainsKey(id))
        {
            return; // the watcher also reports the monitors that were already there at startup
        }

        var window = new NotchWindow(_viewModel);
        var controller = new NotchWindowController(window, _settings.NotchPosition, area);
        window.AttachController(controller);
        window.Activate();
        _notches[id] = (window, controller);
    }

    private void RemoveNotch(ulong displayId)
    {
        if (_notches.Remove(displayId, out var notch))
        {
            notch.Window.Close(); // its Closed handler drops every subscription and timer
        }
    }

    private void WatchDisplays()
    {
        _displayWatcher = DisplayArea.CreateWatcher();
        _displayWatcher.Added += (_, area) => _uiQueue?.TryEnqueue(() => AddNotch(area));
        _displayWatcher.Removed += (_, area) => _uiQueue?.TryEnqueue(() => RemoveNotch(area.DisplayId.Value));
        _displayWatcher.Updated += (_, area) => _uiQueue?.TryEnqueue(() =>
        {
            if (_notches.TryGetValue(area.DisplayId.Value, out var notch))
            {
                notch.Controller.UpdateDisplay(area);
            }
        });
        _displayWatcher.Start();
    }

    private void StartMusicModule(DispatcherQueue dispatcherQueue)
    {
        _mediaProvider = new GsmtcMediaProvider
        {
            PreferredAppToken = MusicPlayerLauncher.SessionToken(_settings.MusicPlayer, _settings.CustomPlayerPath),
        };
        if (_viewModel is not null)
        {
            _viewModel.MediaControls = _mediaProvider;
        }

        _mediaProvider.Changed += (_, info) => dispatcherQueue.TryEnqueue(() =>
        {
            if (_viewModel is not null)
            {
                _viewModel.NowPlaying = info;
            }
        });

        _ = _mediaProvider.InitializeAsync();
    }

    private void StartAgentPolling(DispatcherQueue dispatcherQueue)
    {
        var pricing = PricingCatalog.LoadFrom(Path.Combine(
            Path.GetDirectoryName(_settingsStore.FilePath) ?? ".", "pricing.json"));

        var providers = new List<IAgentUsageProvider>
        {
            new ClaudeCodeUsageProvider(_settings.ClaudeHomeOverride, pricing),
            new CodexUsageProvider(_settings.AdditionalCodexHomes, pricing),
            new CopilotUsageProvider(_settings.CopilotHomeOverride),
            new OpenCodeUsageProvider(_settings.OpenCodeDataOverride),
        };

        _agentPolling = new AgentPollingService(providers, dispatcherQueue, TimeSpan.FromSeconds(_settings.PollIntervalSeconds));
        _agentPolling.SnapshotsUpdated += snapshots =>
        {
            // Record today's spend first so the trend chart drawn by the snapshot update already includes it.
            _viewModel?.History?.Record(
                DateOnly.FromDateTime(DateTime.Now), snapshots.Where(s => s.Error is null).Sum(s => s.EstimatedCostUsdToday ?? 0));
            _viewModel?.ReplaceAgentSnapshots(snapshots);
            foreach (var done in _completion.Observe(snapshots))
            {
                _viewModel?.Notifications.Add(
                    AgentName(done.Kind),
                    "Terminó una tarea",
                    $"Trabajó {(done.WorkedFor.TotalMinutes >= 1 ? $"{(int)done.WorkedFor.TotalMinutes} min" : $"{(int)done.WorkedFor.TotalSeconds} s")}");
            }

            _trayIcon?.UpdateTooltip(BuildTooltip(snapshots));
        };
        _agentPolling.Start();
    }

    private static string AgentName(AgentKind kind) => kind switch
    {
        AgentKind.ClaudeCode => "Claude Code",
        AgentKind.Codex => "Codex",
        AgentKind.Copilot => "Copilot",
        AgentKind.OpenCode => "OpenCode",
        _ => kind.ToString(),
    };

    private void PersistSections()
    {
        _settings.NotchSections = _viewModel?.Sections ?? NotchSections.Defaults();
        if (_hosted is not null)
        {
            // Hosted: WinMac owns this. Remember it locally first so the host's echo is not seen as a change, then ask the host to save it.
            if (_central is not null)
            {
                _central.Sections = NotchSections.Normalize(_settings.NotchSections);
            }

            _hosted.SendPatch(CentralSettings.SectionsPatch(_settings.NotchSections));
        }
        else
        {
            _settingsStore.Save(_settings);
        }

        ApplyClipboardSetting();
    }

    /// <summary>The clipboard is only watched while its tab is enabled; turning the tab off stops recording at once.</summary>
    private void ApplyClipboardSetting()
    {
        if (_viewModel is null || _uiQueue is null)
        {
            return;
        }

        var enabled = _viewModel.Sections.Any(s => s.Id == NotchSections.Clipboard && s.Enabled);
        if (!enabled)
        {
            _clipboardWatcher?.Stop();
            return;
        }

        _clipboardWatcher ??= new ClipboardWatcher(_viewModel.Clipboard, _uiQueue);
        _clipboardWatcher.Start();
    }

    /// <summary>Starts the player chosen in Settings. Returns a message to show when it can't be started, else null.</summary>
    private string? OpenPlayer()
    {
        var name = MusicPlayerLauncher.DisplayName(_settings.MusicPlayer, _settings.CustomPlayerPath);
        var target = MusicPlayerLauncher.Resolve(
            _settings.MusicPlayer, _settings.CustomPlayerPath, File.Exists, Environment.GetEnvironmentVariable);
        if (target is null)
        {
            return $"No encontré {name}. Indica su ruta en Ajustes.";
        }

        try
        {
            Process.Start(new ProcessStartInfo(target.FileName) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"No se pudo abrir {name}.";
        }
    }

    private void ApplyPlayerSettings()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PlayerName = MusicPlayerLauncher.DisplayName(_settings.MusicPlayer, _settings.CustomPlayerPath);
        if (_mediaProvider is not null)
        {
            _mediaProvider.PreferredAppToken = MusicPlayerLauncher.SessionToken(_settings.MusicPlayer, _settings.CustomPlayerPath);
        }

        _viewModel.RaiseDataChanged();
    }

    private void PersistGlass()
    {
        _settings.GlassLevel = _viewModel?.GlassLevel ?? _settings.GlassLevel;
        if (_hosted is not null)
        {
            if (_central is not null)
            {
                _central.GlassLevel = _settings.GlassLevel;
            }

            _hosted.SendPatch(CentralSettings.GlassPatch(_settings.GlassLevel));
        }
        else
        {
            _settingsStore.Save(_settings);
        }
    }

    /// <summary>One shared 1 s clock for every notch window: timer countdown, download scan, calendar refresh.</summary>
    private void StartClock(DispatcherQueue dispatcherQueue)
    {
        ReloadBackgroundSources();

        _clock = dispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) =>
        {
            _clockTicks++;
            _viewModel?.RaiseTicked();
            if (_clockTicks % 2 == 0)
            {
                PollDownloads();
            }

            if (_clockTicks % 60 == 0)
            {
                ReloadCalendar();
            }
        };
        _clock.Start();
    }

    private void ReloadBackgroundSources()
    {
        if (_viewModel is not null)
        {
            _viewModel.Lyrics = _settings.LyricsEnabled ? CreateLyricsClient() : null;
            _viewModel.LyricsInPill = _settings.LyricsEnabled && _settings.LyricsInPill;
        }

        var folder = string.IsNullOrWhiteSpace(_settings.DownloadsFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : _settings.DownloadsFolder;
        _downloads = new DownloadTracker(folder);
        ReloadCalendar();
    }

    private static NeteaseLyricsClient CreateLyricsClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NotchWin/0.1 (https://github.com/pipesick/notch-win)");
        return new NeteaseLyricsClient(http);
    }

    private void PollDownloads()
    {
        if (_downloads is null || _viewModel is null)
        {
            return;
        }

        var (active, completed) = _downloads.Poll();
        foreach (var name in completed)
        {
            _viewModel.Notifications.Add("Descargas", name, "Descarga terminada");
        }

        var previous = _viewModel.Downloads;
        var changed = completed.Count > 0
                      || previous.Count != active.Count
                      || previous.Zip(active).Any(p => p.First != p.Second);
        _viewModel.Downloads = active;
        if (changed)
        {
            _viewModel.RaiseDataChanged();
        }
    }

    private void ReloadCalendar()
    {
        if (_viewModel is null)
        {
            return;
        }

        var path = _settings.CalendarIcsPath;
        _viewModel.CalendarConfigured = !string.IsNullOrWhiteSpace(path);

        var today = new List<CalendarEvent>();
        if (_viewModel.CalendarConfigured)
        {
            try
            {
                today = IcsParser.UpcomingToday(IcsParser.Parse(File.ReadAllText(path!)), DateTimeOffset.Now);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Missing/locked file (e.g. mid-sync): show an empty day rather than a stale or broken list.
            }
        }

        _viewModel.CalendarToday = today;
        _viewModel.RaiseDataChanged();
    }

    private static string BuildTooltip(IReadOnlyList<AgentUsageSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return "NotchWin";
        }

        var parts = snapshots
            .Where(s => s.Error is null)
            .Select(s => $"{s.Kind}: {s.TokensToday.Total} tok");
        return "NotchWin — " + string.Join(" · ", parts);
    }

    // ---------- hosted by WinMac ----------

    /// <summary>
    /// Connects to WinMac when this process was started by it. Waits briefly for the first settings so the notch is built with the right ones instead of flashing the old
    /// local values; if the host is slow or absent the notch starts with what it has and applies the settings whenever they arrive.
    /// </summary>
    private void StartHostedLink()
    {
        _hosted = Program.Hosted;
        if (_hosted is null)
        {
            return;
        }

        _hosted.HealthCheck = UiThreadIsAlive;
        _hosted.ShutdownRequested += () => _uiQueue?.TryEnqueue(ExitApp);
        _hosted.OpenLocalSettingsRequested += () => _uiQueue?.TryEnqueue(OpenSettingsWindow);
        _hosted.CentralReceived += central =>
        {
            _firstCentral ??= central;
            _firstCentralArrived.Set();
            _uiQueue?.TryEnqueue(() => ApplyCentral(central));
        };
        _hosted.Start();

        if (_firstCentralArrived.Wait(TimeSpan.FromSeconds(1.5)) && _firstCentral is { } first)
        {
            first.ApplyTo(_settings);
        }

        _central = CentralSettings.From(_settings);
    }

    /// <summary>Answers WinMac's heartbeat only while the UI thread is actually processing messages, so a hung notch gets restarted.</summary>
    private bool UiThreadIsAlive()
    {
        if (_uiQueue is null)
        {
            return true;
        }

        using var done = new ManualResetEventSlim();
        return _uiQueue.TryEnqueue(() => done.Set()) && done.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>Applies new central settings from WinMac live, touching only what changed. Runs on the UI thread.</summary>
    private void ApplyCentral(CentralSettings next)
    {
        if (_viewModel is null || _uiQueue is null)
        {
            return;
        }

        var changes = CentralSettings.Diff(_central ?? CentralSettings.From(_settings), next);
        _central = next;
        if (changes == CentralChanges.None)
        {
            return;
        }

        next.ApplyTo(_settings);

        if (changes.HasFlag(CentralChanges.Sections))
        {
            _viewModel.Sections = NotchSections.Normalize(_settings.NotchSections);
            _viewModel.RaiseSectionsChanged();
            ApplyClipboardSetting();
        }

        if (changes.HasFlag(CentralChanges.Glass))
        {
            _viewModel.GlassLevel = _settings.GlassLevel;
        }

        if (changes.HasFlag(CentralChanges.Player))
        {
            ApplyPlayerSettings();
        }

        if (changes.HasFlag(CentralChanges.Lyrics))
        {
            _viewModel.Lyrics = _settings.LyricsEnabled ? CreateLyricsClient() : null;
            _viewModel.LyricsInPill = _settings.LyricsEnabled && _settings.LyricsInPill;
            _viewModel.RaiseDataChanged();
        }

        if (changes.HasFlag(CentralChanges.MusicModule))
        {
            StopMusicModule();
            if (_settings.MusicModuleEnabled)
            {
                StartMusicModule(_uiQueue);
            }
        }

        if (changes.HasFlag(CentralChanges.AgentsModule) || changes.HasFlag(CentralChanges.Poll))
        {
            StopAgentPolling();
            if (_settings.AiAgentsModuleEnabled)
            {
                StartAgentPolling(_uiQueue);
            }
        }

        if (changes.HasFlag(CentralChanges.Position))
        {
            RecreateNotches(); // the position is fixed per window; rebuilding them keeps the resize/DPI rules in NotchWindowController untouched
        }
    }

    private void StopMusicModule()
    {
        _mediaProvider?.Dispose();
        _mediaProvider = null;
        if (_viewModel is not null)
        {
            _viewModel.MediaControls = null;
            _viewModel.NowPlaying = null;
            _viewModel.RaiseDataChanged();
        }
    }

    private void StopAgentPolling()
    {
        _agentPolling?.Dispose();
        _agentPolling = null;
    }

    private void RecreateNotches()
    {
        foreach (var id in _notches.Keys.ToList())
        {
            RemoveNotch(id);
        }

        var displays = DisplayArea.FindAll();
        for (var i = 0; i < displays.Count; i++)
        {
            AddNotch(displays[i]);
        }
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsStore, hosted: _hosted is not null);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.SettingsSaved += saved =>
        {
            _settings = saved;
            _central?.ApplyTo(_settings); // hosted: the file's central fields are stale; the host's values win
            ReloadBackgroundSources();
            ApplyPlayerSettings();
        };
        _settingsWindow.Activate();
    }

    private void ExitApp()
    {
        if (_hosted is not null)
        {
            // Tell WinMac this exit is deliberate, then drop the link. Bounded wait: never hold the UI thread hostage.
            try { Task.Run(() => _hosted.ByeAsync(TimeSpan.FromSeconds(1))).Wait(TimeSpan.FromSeconds(1.5)); } catch (Exception) { /* exiting anyway */ }
            _ = _hosted.DisposeAsync();
        }

        _displayWatcher?.Stop();
        _agentPolling?.Dispose();
        _mediaProvider?.Dispose();
        _trayIcon?.Dispose();
        Current.Exit();
    }
}
