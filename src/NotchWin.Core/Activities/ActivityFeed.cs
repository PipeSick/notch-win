using System.Text.Json;

namespace NotchWin.Core.Activities;

/// <summary>
/// INTERNAL activity API on the host's IPC channel (topic <see cref="Topic"/>): only WinMac's own modules reach it (named pipe restricted to the current user,
/// session token). Messages: <c>{ "op": "upsert" | "finish" | "remove", "activity": { "id", "kind", "title", "subtitle", "source", "progress", "state", "path",
/// "thumbnail", "test" } }</c>. Every value is validated; ids from outside are prefixed with «ipc:» so they can never replace an activity the Island itself owns.
/// </summary>
public static class ActivityFeed
{
    public const string Topic = "notch.activity";

    public sealed record Command(string Op, Activity? Activity, string? Id, ActivityState State);

    public static Command? Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || Str(e, "op") is not { } op) return null;
        op = op.ToLowerInvariant();
        if (!e.TryGetProperty("activity", out var a) || a.ValueKind != JsonValueKind.Object || Str(a, "id") is not { } rawId) return null;
        string id = "ipc:" + rawId.Trim();
        if (rawId.Length is 0 or > 120) return null;
        var state = Enum.TryParse<ActivityState>(Str(a, "state"), true, out var st) && Enum.IsDefined(st) ? st : ActivityState.Active;
        switch (op)
        {
            case "finish": return new Command(op, null, id, state == ActivityState.Active ? ActivityState.Completed : state);
            case "remove": return new Command(op, null, id, state);
            case "upsert":
                if (!Enum.TryParse<ActivityKind>(Str(a, "kind"), true, out var kind) || !Enum.IsDefined(kind) || Str(a, "title") is not { } title) return null;
                double? progress = a.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d) ? d : null;
                return new Command(op, new Activity(id, kind, title)
                {
                    Subtitle = Str(a, "subtitle"), Source = Str(a, "source"), Progress = progress, State = state,
                    Path = SafePath(Str(a, "path")), ThumbnailPath = SafePath(Str(a, "thumbnail")),
                    IsTest = a.TryGetProperty("test", out var t) && t.ValueKind == JsonValueKind.True,
                }, id, state);
            default: return null;
        }
    }

    /// <summary>Applies a command to the store. Returns false if it did nothing.</summary>
    public static bool Apply(ActivityStore store, Command c) => c.Op switch
    {
        "upsert" when c.Activity is { } a => store.Upsert(a),
        "finish" when c.Id is { } id => store.Finish(id, c.State),
        "remove" when c.Id is { } id => store.Remove(id),
        _ => false,
    };

    private static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Only absolute local paths (no URLs, no UNC shares): the Island may open or show them.</summary>
    private static string? SafePath(string? p) =>
        p is { Length: > 3 and < 400 } && char.IsLetter(p[0]) && p[1] == ':' && p[2] is '\\' or '/' && !p.Contains("..", StringComparison.Ordinal) ? p : null;
}

/// <summary>What the collapsed Island shows as its usual content (transient items from <c>PillArbiter</c> go on top and then give it back).</summary>
public enum CompactContent { Idle, Lyrics, Track, Download, Timer }

public static class CompactPolicy
{
    /// <summary>
    /// The SAME lyrics rule NotchWin always had (lyrics in the pill only while the song plays, with lyrics on, «letra en el notch» on and lines loaded),
    /// then the track, then a download in progress, then the timer. Synced lyrics are never displaced by the new activities.
    /// </summary>
    public static CompactContent Choose(bool hasTrack, bool isPlaying, bool lyricsOn, bool lyricsInPill, int lyricLines, bool downloadActive, bool timerRunning)
    {
        if (hasTrack && isPlaying && lyricsOn && lyricsInPill && lyricLines > 0) return CompactContent.Lyrics;
        if (hasTrack) return CompactContent.Track;
        if (downloadActive) return CompactContent.Download;
        return timerRunning ? CompactContent.Timer : CompactContent.Idle;
    }
}
