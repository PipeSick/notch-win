using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NotchWin.App.Modules.Music;
using NotchWin.App.Modules.Notch;
using NotchWin.App.Services;
using NotchWin.App.ViewModels;
using NotchWin.App.Views;
using NotchWin.Core.AiAgents;
using NotchWin.Core.AiAgents.ClaudeCode;
using NotchWin.Core.AiAgents.Codex;
using NotchWin.Core.AiAgents.Copilot;
using NotchWin.Core.Calendar;
using NotchWin.Core.Downloads;
using NotchWin.Core.Models;
using NotchWin.Core.Pricing;
using NotchWin.Core.Settings;

namespace NotchWin.App;

public partial class App : Application
{
    private readonly SettingsStore _settingsStore = new(SettingsStore.DefaultPath());
    private AppSettings _settings = new();

    private readonly List<NotchWindow> _notchWindows = [];
    private NotchViewModel? _viewModel;
    private GsmtcMediaProvider? _mediaProvider;
    private AgentPollingService? _agentPolling;
    private TrayIconService? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private readonly AgentCompletionDetector _completion = new();
    private DownloadTracker? _downloads;
    private DispatcherQueueTimer? _clock;
    private int _clockTicks;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _settings = _settingsStore.Load();

        _viewModel = new NotchViewModel
        {
            Sections = NotchSections.Normalize(_settings.NotchSections),
            PersistSections = PersistSections,
        };
        // One notch per monitor, all sharing the same view model.
        var displays = DisplayArea.FindAll();
        for (var i = 0; i < displays.Count; i++)
        {
            var window = new NotchWindow(_viewModel);
            window.AttachController(new NotchWindowController(window, _settings.NotchPosition, displays[i]));
            window.Activate();
            _notchWindows.Add(window);
        }

        var dispatcherQueue = _notchWindows[0].DispatcherQueue;

        StartClock(dispatcherQueue);

        _trayIcon = new TrayIconService("NotchWin");
        _trayIcon.SettingsRequested += () => dispatcherQueue.TryEnqueue(OpenSettingsWindow);
        _trayIcon.ExitRequested += () => dispatcherQueue.TryEnqueue(ExitApp);

        if (_settings.MusicModuleEnabled)
        {
            StartMusicModule(dispatcherQueue);
        }

        if (_settings.AiAgentsModuleEnabled)
        {
            StartAgentPolling(dispatcherQueue);
        }
    }

    private void StartMusicModule(DispatcherQueue dispatcherQueue)
    {
        _mediaProvider = new GsmtcMediaProvider();
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
        };

        _agentPolling = new AgentPollingService(providers, dispatcherQueue, TimeSpan.FromSeconds(_settings.PollIntervalSeconds));
        _agentPolling.SnapshotsUpdated += snapshots =>
        {
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
        _ => kind.ToString(),
    };

    private void PersistSections()
    {
        _settings.NotchSections = _viewModel?.Sections ?? NotchSections.Defaults();
        _settingsStore.Save(_settings);
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
        var folder = string.IsNullOrWhiteSpace(_settings.DownloadsFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : _settings.DownloadsFolder;
        _downloads = new DownloadTracker(folder);
        ReloadCalendar();
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

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsStore);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.SettingsSaved += saved =>
        {
            _settings = saved;
            ReloadBackgroundSources();
        };
        _settingsWindow.Activate();
    }

    private void ExitApp()
    {
        _agentPolling?.Dispose();
        _mediaProvider?.Dispose();
        _trayIcon?.Dispose();
        Current.Exit();
    }
}
