using Microsoft.UI.Dispatching;
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
using NotchWin.Core.Models;
using NotchWin.Core.Pricing;
using NotchWin.Core.Settings;

namespace NotchWin.App;

public partial class App : Application
{
    private readonly SettingsStore _settingsStore = new(SettingsStore.DefaultPath());
    private AppSettings _settings = new();

    private NotchWindow? _notchWindow;
    private NotchWindowController? _notchController;
    private NotchViewModel? _viewModel;
    private GsmtcMediaProvider? _mediaProvider;
    private AgentPollingService? _agentPolling;
    private TrayIconService? _trayIcon;
    private SettingsWindow? _settingsWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _settings = _settingsStore.Load();

        _viewModel = new NotchViewModel();
        _notchWindow = new NotchWindow(_viewModel);
        _notchController = new NotchWindowController(_notchWindow, _settings.NotchPosition);
        _notchWindow.AttachController(_notchController);
        _notchWindow.Activate();

        var dispatcherQueue = _notchWindow.DispatcherQueue;

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
            _trayIcon?.UpdateTooltip(BuildTooltip(snapshots));
        };
        _agentPolling.Start();
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
