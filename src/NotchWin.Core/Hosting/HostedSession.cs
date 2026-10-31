using System.Text.Json;
using WinMac.Ipc;

namespace NotchWin.Core.Hosting;

/// <summary>
/// NotchWin running as a module of WinMac (started with <c>--hosted --pipe &lt;name&gt;</c> and the session token in the environment). Without those it does not exist and NotchWin runs
/// exactly as before, standalone. If the host goes away the notch keeps working with the last settings it received and the link keeps trying to reconnect.
/// </summary>
public sealed class HostedSession : IAsyncDisposable
{
    private readonly ModuleHostLink _link;

    public HostedSession(ModuleHostLink link)
    {
        _link = link;
        _link.ShutdownRequested += () => ShutdownRequested?.Invoke();
        _link.Subscribe(CentralSettings.Topic, OnCentral);
        _link.Subscribe(CentralSettings.CommandTopic, OnCommand);
    }

    /// <summary>Null when not started by the host (no <c>--hosted</c>, no pipe or no token).</summary>
    public static HostedSession? FromArgs(string[] args, string version = "0")
    {
        var link = ModuleHostLink.FromArgs(args, "notch", version);
        return link is null ? null : new HostedSession(link);
    }

    /// <summary>The host's settings arrived (on connect and after every change). Raised on a worker thread.</summary>
    public event Action<CentralSettings>? CentralReceived;

    /// <summary>The host asks to open NotchWin's own window for the local data (folders, calendar file). Raised on a worker thread.</summary>
    public event Action? OpenLocalSettingsRequested;

    /// <summary>The host asked to close: leave through the normal path. Raised on a worker thread.</summary>
    public event Action? ShutdownRequested;

    public bool IsConnected => _link.IsConnected;

    /// <summary>Answer to the host's heartbeat: return false while the UI thread is blocked, so a hung notch is restarted.</summary>
    public Func<bool>? HealthCheck { get => _link.HealthCheck; set => _link.HealthCheck = value; }

    public void Start() => _link.Start();

    /// <summary>Ask the host to change settings; send only the fields that changed. The host validates, saves and echoes the new state back.</summary>
    public void SendPatch(object patch) => _link.Event(CentralSettings.SetTopic, patch);

    /// <summary>
    /// Publishes the screen space (pixels) the notch windows occupy, including what an animation is about to cover. WinMac's top bar cuts that space out of its own window so it never
    /// covers the notch or takes its clicks, whatever the window order. An empty list releases it.
    /// </summary>
    public void PublishRects(IEnumerable<(int X, int Y, int Width, int Height)> rects) =>
        _link.Publish(CentralSettings.RectsTopic, new { rects = rects.Where(r => r.Width > 0 && r.Height > 0).Select(r => new { x = r.X, y = r.Y, w = r.Width, h = r.Height }).ToArray() });

    /// <summary>Tell the host this exit is deliberate (it must not restart us).</summary>
    public Task ByeAsync(TimeSpan timeout) => _link.ByeAsync(timeout);

    private void OnCentral(JsonElement? data)
    {
        if (data is { } d && CentralSettings.FromJson(d) is { } c)
        {
            CentralReceived?.Invoke(c);
        }
    }

    private void OnCommand(JsonElement? data)
    {
        if (data is { } d && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("cmd", out var cmd) && cmd.ValueKind == JsonValueKind.String && cmd.GetString() == "open-local-settings")
        {
            OpenLocalSettingsRequested?.Invoke();
        }
    }

    public ValueTask DisposeAsync() => _link.DisposeAsync();
}
