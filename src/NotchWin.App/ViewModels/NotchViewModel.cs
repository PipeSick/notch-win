using System.Collections.ObjectModel;
using NotchWin.Core.Models;
using NotchWin.Core.Music;

namespace NotchWin.App.ViewModels;

/// <summary>Bindable state for the notch window: current now-playing track and the latest AI agent snapshots.</summary>
public sealed class NotchViewModel : ObservableObject
{
    private NowPlayingInfo? _nowPlaying;
    private bool _isExpanded;

    public NowPlayingInfo? NowPlaying
    {
        get => _nowPlaying;
        set => SetProperty(ref _nowPlaying, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
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
