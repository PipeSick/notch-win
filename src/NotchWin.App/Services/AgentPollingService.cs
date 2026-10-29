using Microsoft.UI.Dispatching;
using NotchWin.Core.AiAgents;
using NotchWin.Core.Models;

namespace NotchWin.App.Services;

/// <summary>Periodically polls every registered <see cref="IAgentUsageProvider"/> and republishes
/// the combined snapshots on the UI thread.</summary>
public sealed class AgentPollingService : IDisposable
{
    private readonly IReadOnlyList<IAgentUsageProvider> _providers;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _timer;

    public event Action<IReadOnlyList<AgentUsageSnapshot>>? SnapshotsUpdated;

    public AgentPollingService(IReadOnlyList<IAgentUsageProvider> providers, DispatcherQueue dispatcherQueue, TimeSpan interval)
    {
        _providers = providers;
        _dispatcherQueue = dispatcherQueue;

        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = interval;
        _timer.Tick += async (_, _) => await PollOnceAsync();
    }

    public void Start()
    {
        _timer.Start();
        _ = PollOnceAsync();
    }

    public async Task PollOnceAsync()
    {
        var all = new List<AgentUsageSnapshot>();
        foreach (var provider in _providers)
        {
            try
            {
                var snapshots = await provider.GetSnapshotsAsync();
                all.AddRange(snapshots);
            }
            catch (Exception)
            {
                // A single misbehaving provider (e.g. an IO error we didn't anticipate) must
                // never take down the whole tray/notch update loop.
            }
        }

        _dispatcherQueue.TryEnqueue(() => SnapshotsUpdated?.Invoke(all));
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
