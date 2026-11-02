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
        _link.Subscribe(Pill.PillFeed.Topic, OnFeed);
        _link.Subscribe(Activities.ActivityFeed.Topic, OnActivity);
        _link.Subscribe(BarTopic, OnBar);
        _link.Subscribe(ClipQueryTopic, d => { if (d is { ValueKind: JsonValueKind.Object } e && Num(e, "req") is { } r) ClipQuery?.Invoke(r, e.TryGetProperty("q", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString() : null); });
        _link.Subscribe(ClipActionTopic, d =>
        {
            if (d is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParseExact(id.GetString(), "N", out var g)
                && e.TryGetProperty("op", out var op) && op.GetString() is "copy" or "plain" or "delete")
                ClipAction?.Invoke(g, op.GetString()!);
        });
    }

    /// <summary>Spotlight (WinMac's bar) searches the clipboard history: <c>clip.query {req, q}</c> → <c>clip.results {req, enabled, items}</c>; actions <c>clip.action {id, op}</c>.</summary>
    public const string ClipQueryTopic = "clip.query", ClipResultsTopic = "clip.results", ClipActionTopic = "clip.action";

    /// <summary>(request number, text). Raised on a worker thread.</summary>
    public event Action<long, string?>? ClipQuery;
    /// <summary>(entry id, "copy" | "plain" | "delete"). Raised on a worker thread.</summary>
    public event Action<Guid, string>? ClipAction;

    public void SendClipResults(long req, bool enabled, IEnumerable<Clipboard.ClipboardQuery.Item> items) =>
        _link.Event(ClipResultsTopic, new { req, enabled, items = items.Select(i => new { id = i.Id, kind = i.Kind, preview = i.Preview, pinned = i.Pinned, at = i.At }).ToArray() });

    private static long? Num(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long x) ? x : null;

    /// <summary>The bar's settings (retained). The Island only reads the capture folder from them, so its gallery shows where WinMac saves.</summary>
    public const string BarTopic = "config.bar", EditTopic = "capture.edit";

    /// <summary>The capture folder WinMac's bar saves to (null = Windows' default «Pictures\Screenshots»). Raised on a worker thread.</summary>
    public event Action<string?>? CaptureFolderReceived;

    /// <summary>Asks WinMac's bar to open its capture editor on this file (the bar validates it again).</summary>
    public void RequestEdit(string path) => _link.Event(EditTopic, new { path });

    private void OnBar(JsonElement? data)
    {
        if (data is { ValueKind: JsonValueKind.Object } d)
        {
            CaptureFolderReceived?.Invoke(ParseCaptureFolder(d));
        }
    }

    /// <summary>Only an absolute folder path is accepted; anything else means the default folder.</summary>
    public static string? ParseCaptureFolder(JsonElement bar) =>
        bar.TryGetProperty("captureFolder", out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } f && System.IO.Path.IsPathFullyQualified(f.Trim()) ? f.Trim() : null;

    /// <summary>Null when not started by the host (no <c>--hosted</c>, no pipe or no token).</summary>
    public static HostedSession? FromArgs(string[] args, string version = "0")
    {
        var link = ModuleHostLink.FromArgs(args, "notch", version);
        return link is null ? null : new HostedSession(link);
    }

    /// <summary>The host's settings arrived (on connect and after every change). Raised on a worker thread.</summary>
    public event Action<CentralSettings>? CentralReceived;

    /// <summary>WinMac's top bar relayed a volume/brightness change or a Windows notification for the collapsed notch. Raised on a worker thread.</summary>
    public event Action<Pill.PillItem>? PillReceived;

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

    private void OnFeed(JsonElement? data)
    {
        if (data is { } d && Pill.PillFeed.Parse(d) is { } item)
        {
            PillReceived?.Invoke(item);
        }
    }

    /// <summary>Internal activity API (only WinMac's modules reach the pipe). Raised on a worker thread.</summary>
    public event Action<Activities.ActivityFeed.Command>? ActivityReceived;

    private void OnActivity(JsonElement? data)
    {
        if (data is { } d && Activities.ActivityFeed.Parse(d) is { } c)
        {
            ActivityReceived?.Invoke(c);
        }
    }

    /// <summary>WinMac opens something that must not be covered (capture overlay, editor, recording): the open Island closes. Raised on a worker thread.</summary>
    public event Action? CollapseRequested;

    private void OnCommand(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } d || !d.TryGetProperty("cmd", out var cmd) || cmd.ValueKind != JsonValueKind.String) return;
        switch (cmd.GetString())
        {
            case "open-local-settings": OpenLocalSettingsRequested?.Invoke(); break;
            case "collapse": CollapseRequested?.Invoke(); break;
        }
    }

    public ValueTask DisposeAsync() => _link.DisposeAsync();
}
