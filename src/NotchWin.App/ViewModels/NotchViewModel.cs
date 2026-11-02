using System.Collections.ObjectModel;
using NotchWin.App.Modules.Music;
using WinMac.Calendar;
using NotchWin.Core.Clipboard;
using NotchWin.Core.Usage;
using NotchWin.Core.Downloads;
using NotchWin.Core.Lyrics;
using NotchWin.Core.Models;
using NotchWin.Core.Notifications;
using NotchWin.Core.Pill;
using NotchWin.Core.Hosting;
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
    public NeteaseLyricsClient? Lyrics { get; set; }

    public NotificationLog Notifications { get; } = new();

    public IReadOnlyList<ActiveDownload> Downloads { get; set; } = [];

    public IReadOnlyList<CalendarEvent> CalendarToday { get; set; } = [];

    /// <summary>False until the user points Settings at an .ics file, so the tab can say how to set it up.</summary>
    public bool CalendarConfigured { get; set; }

    public List<NotchSectionConfig> Sections { get; set; } = NotchSections.Defaults();

    /// <summary>Called after the user reorders/toggles tabs so the host can write settings to disk.</summary>
    public Action? PersistSections { get; set; }

    /// <summary>Daily spend for the agents tab's trend chart; null until the app wires it up.</summary>
    public UsageHistory? History { get; set; }

    /// <summary>Clipboard history shown in the clipboard tab. It only fills while that section is enabled.</summary>
    public ClipboardHistory Clipboard { get; } = new();

    /// <summary>When true and lyrics are loaded, the collapsed notch shows the current lyric line instead of the title.</summary>
    public bool LyricsInPill { get; set; }

    /// <summary>Name of the chosen music player, shown on the "Abrir …" button when nothing is playing.</summary>
    public string PlayerName { get; set; } = "Spotify";

    /// <summary>Starts the chosen player; returns an error message to show, or null on success.</summary>
    public Func<string?>? OpenPlayer { get; set; }

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

    /// <summary>Priorities and durations of what the collapsed notch shows for a moment; every notch window has its own arbiter built on it.</summary>
    public PillPolicy PillPolicy { get; private set; } = PillPolicy.FromSettings();

    /// <summary>False: the pill changes size at once instead of animating (setting «Animaciones»).</summary>
    public bool PillAnimations { get; private set; } = true;

    /// <summary>Raised after <see cref="PillPolicy"/> or <see cref="PillAnimations"/> changed.</summary>
    public event Action? PillPolicyChanged;

    /// <summary>Something to show for a moment in the collapsed notch (volume, brightness, a Windows notification). Every notch window decides with its arbiter.</summary>
    public event Action<PillItem>? PillPosted;

    public void PostPill(PillItem item) => PillPosted?.Invoke(item);

    public void ApplyPill(CentralSettings c)
    {
        PillPolicy = c.PillPolicy();
        PillAnimations = c.PillAnimations;
        PillPolicyChanged?.Invoke();
    }

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
