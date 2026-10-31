using System.Collections.ObjectModel;
using NotchWin.App.Modules.Music;
using NotchWin.Core.Calendar;
using NotchWin.Core.Downloads;
using NotchWin.Core.Lyrics;
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

    /// <summary>Files on the shelf (full paths). Kept for the session only; the originals are never copied or moved.</summary>
    public List<string> ShelfFiles { get; } = [];

    /// <summary>Null unless the user turned lyrics on in Settings.</summary>
    public LyricsProvider? Lyrics { get; set; }

    public NotificationLog Notifications { get; } = new();

    public IReadOnlyList<ActiveDownload> Downloads { get; set; } = [];

    public IReadOnlyList<CalendarEvent> CalendarToday { get; set; } = [];

    /// <summary>False until the user points Settings at an .ics file, so the tab can say how to set it up.</summary>
    public bool CalendarConfigured { get; set; }

    public List<NotchSectionConfig> Sections { get; set; } = NotchSections.Defaults();

    /// <summary>Called after the user reorders/toggles tabs so the host can write settings to disk.</summary>
    public Action? PersistSections { get; set; }

    private int _glassLevel = 60;

    /// <summary>Glass opacity 0–100 shared by every notch window; setting it re-tints them live.</summary>
    public int GlassLevel
    {
        get => _glassLevel;
        set
        {
            if (SetProperty(ref _glassLevel, Math.Clamp(value, 0, 100)))
            {
                GlassChanged?.Invoke();
            }
        }
    }

    /// <summary>Called after the user moves the glass slider so the host can write settings to disk.</summary>
    public Action? PersistGlass { get; set; }

    public event Action? GlassChanged;

    /// <summary>Fires once a second from the app's single clock (timer countdown, progress).</summary>
    public event Action? Ticked;

    /// <summary>Fires when downloads, calendar or similar background data was refreshed.</summary>
    public event Action? DataChanged;

    /// <summary>Fires when the tab order or visibility changed, so every notch window rebuilds its tabs.</summary>
    public event Action? SectionsChanged;

    public void RaiseSectionsChanged() => SectionsChanged?.Invoke();

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
