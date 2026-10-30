using System.Collections.ObjectModel;
using NotchWin.App.Modules.Music;
using NotchWin.Core.Models;
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
