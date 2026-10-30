using System.Collections.ObjectModel;
using NotchWin.App.Modules.Music;
using NotchWin.Core.Calendar;
using NotchWin.Core.Downloads;
using NotchWin.Core.Models;
using NotchWin.Core.Notifications;
using NotchWin.Core.Settings;
using NotchWin.Core.Timers;
using NotchWin.Core.Music;

namespace NotchWin.App.ViewModels;

/// <summary>Bindable state for the notch window: current now-playing track and the latest AI agent snapshots.</summary>
public sealed class NotchViewModel : ObservableObject
{
    private NowPlayingInfo? _nowPlaying;

    public NowPlayingInfo? NowPlaying
    {
        get => _nowPlaying;
        set => SetProperty(ref _nowPlaying, value);
    }

    public CountdownTimer Timer { get; } = new();

    public NotificationLog Notifications { get; } = new();

    public IReadOnlyList<ActiveDownload> Downloads { get; set; } = [];

    public IReadOnlyList<CalendarEvent> CalendarToday { get; set; } = [];

    /// <summary>False until the user points Settings at an .ics file, so the tab can say how to set it up.</summary>
    public bool CalendarConfigured { get; set; }

    public List<NotchSectionConfig> Sections { get; set; } = NotchSections.Defaults();

    /// <summary>Called after the user reorders/toggles tabs so the host can write settings to disk.</summary>
    public Action? PersistSections { get; set; }

    /// <summary>Fires once a second from the app's single clock (timer countdown, progress).</summary>
    public event Action? Ticked;

    /// <summary>Fires when downloads, calendar or similar background data was refreshed.</summary>
    public event Action? DataChanged;

    public void RaiseTicked() => Ticked?.Invoke();

    public void RaiseDataChanged() => DataChanged?.Invoke();

    /// <summary>Set once the music module starts; null while it's disabled (the player shows no controls then).</summary>
    public IMediaControls? MediaControls { get; set; }

    public ObservableCollection<AgentUsageSnapshot> AgentSnapshots { get; } = [];

    public void ReplaceAgentSnapshots(IReadOnlyList<AgentUsageSnapshot> snapshots)
    {
        AgentSnapshots.Clear();
        foreach (var snapshot in snapshots)
        {
            AgentSnapshots.Add(snapshot);
        }
    }
}
