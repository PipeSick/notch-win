using NotchWin.Core.Models;

namespace NotchWin.Core.AiAgents;

public sealed record AgentCompletion(AgentKind Kind, string AccountLabel, TimeSpan WorkedFor);

/// <summary>
/// Turns the "is this agent mid-turn" flag from successive polls into a "finished a long task" event:
/// a transition from working to idle that followed at least <paramref name="minimumWork"/> of work.
/// The threshold keeps quick one-line replies from notifying. Resolution is the poll interval, so a
/// task shorter than one poll can't be seen at all.
/// </summary>
public sealed class AgentCompletionDetector(TimeSpan? minimumWork = null, Func<DateTimeOffset>? clock = null)
{
    private readonly TimeSpan _minimumWork = minimumWork ?? TimeSpan.FromSeconds(60);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Dictionary<(AgentKind, string), DateTimeOffset> _workingSince = [];

    public List<AgentCompletion> Observe(IEnumerable<AgentUsageSnapshot> snapshots)
    {
        var now = _clock();
        var completions = new List<AgentCompletion>();

        foreach (var snapshot in snapshots.Where(s => s.Error is null))
        {
            var key = (snapshot.Kind, snapshot.AccountLabel);
            var wasWorking = _workingSince.TryGetValue(key, out var since);

            if (snapshot.IsLiveWorking && !wasWorking)
            {
                _workingSince[key] = now;
            }
            else if (!snapshot.IsLiveWorking && wasWorking)
            {
                _workingSince.Remove(key);
                if (now - since >= _minimumWork)
                {
                    completions.Add(new AgentCompletion(snapshot.Kind, snapshot.AccountLabel, now - since));
                }
            }
        }

        return completions;
    }
}
