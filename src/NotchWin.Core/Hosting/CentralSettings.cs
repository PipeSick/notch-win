using System.Text.Json;
using NotchWin.Core.Music;
using NotchWin.Core.Settings;

namespace NotchWin.Core.Hosting;

/// <summary>What changed between two central settings; used to apply a new value live and only where needed.</summary>
[Flags]
public enum CentralChanges
{
    None = 0,
    Position = 1,
    Glass = 2,
    Sections = 4,
    MusicModule = 8,
    AgentsModule = 16,
    Player = 32,
    Lyrics = 64,
    Poll = 128,
}

/// <summary>
/// The settings WinMac owns when NotchWin runs inside it (the host's winmac.json is the single source of truth). Wire format: camelCase JSON on the retained topic
/// <c>config.notch</c> (host to notch); the notch asks for changes by sending a patch with only the changed fields on <c>config.notch.set</c>.
/// Everything not listed here (data folders, calendar file, usage history, prices) stays local to NotchWin, and no secret is ever part of it.
/// </summary>
public sealed class CentralSettings
{
    public const string Topic = "config.notch", SetTopic = "config.notch.set", CommandTopic = "notch.cmd", RectsTopic = "notch.rects";

    public NotchPosition Position { get; set; } = NotchPosition.TopCenter;
    public int GlassLevel { get; set; } = 60;
    public List<NotchSectionConfig> Sections { get; set; } = NotchSections.Defaults();
    public bool MusicEnabled { get; set; } = true;
    public bool AgentsEnabled { get; set; } = true;
    public MusicPlayerKind MusicPlayer { get; set; } = MusicPlayerKind.Spotify;
    public string? CustomPlayerPath { get; set; }
    public bool LyricsEnabled { get; set; }
    public bool LyricsInPill { get; set; }
    public int PollIntervalSeconds { get; set; } = 30;

    public static CentralSettings From(AppSettings s) => new()
    {
        Position = s.NotchPosition,
        GlassLevel = s.GlassLevel,
        Sections = NotchSections.Normalize(s.NotchSections),
        MusicEnabled = s.MusicModuleEnabled,
        AgentsEnabled = s.AiAgentsModuleEnabled,
        MusicPlayer = s.MusicPlayer,
        CustomPlayerPath = s.CustomPlayerPath,
        LyricsEnabled = s.LyricsEnabled,
        LyricsInPill = s.LyricsInPill,
        PollIntervalSeconds = s.PollIntervalSeconds,
    };

    /// <summary>Copies the central fields into the local settings object (it never writes the file: in hosted mode the host persists them).</summary>
    public void ApplyTo(AppSettings s)
    {
        s.NotchPosition = Position;
        s.GlassLevel = GlassLevel;
        s.NotchSections = NotchSections.Normalize(Sections);
        s.MusicModuleEnabled = MusicEnabled;
        s.AiAgentsModuleEnabled = AgentsEnabled;
        s.MusicPlayer = MusicPlayer;
        s.CustomPlayerPath = CustomPlayerPath;
        s.LyricsEnabled = LyricsEnabled;
        s.LyricsInPill = LyricsInPill;
        s.PollIntervalSeconds = PollIntervalSeconds;
    }

    /// <summary>Parses the host's message; unknown fields are ignored and invalid values keep the default. Returns null when it is not an object.</summary>
    public static CentralSettings? FromJson(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var c = new CentralSettings();
        foreach (var p in e.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name.ToLowerInvariant())
            {
                case "position" when v.ValueKind == JsonValueKind.String && Enum.TryParse<NotchPosition>(v.GetString(), true, out var pos) && Enum.IsDefined(pos):
                    c.Position = pos;
                    break;
                case "glasslevel" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var g):
                    c.GlassLevel = Math.Clamp(g, 0, 100);
                    break;
                case "musicenabled" when IsBool(v):
                    c.MusicEnabled = v.GetBoolean();
                    break;
                case "agentsenabled" when IsBool(v):
                    c.AgentsEnabled = v.GetBoolean();
                    break;
                case "musicplayer" when v.ValueKind == JsonValueKind.String && Enum.TryParse<MusicPlayerKind>(v.GetString(), true, out var mp) && Enum.IsDefined(mp):
                    c.MusicPlayer = mp;
                    break;
                case "customplayerpath" when v.ValueKind is JsonValueKind.String or JsonValueKind.Null:
                    c.CustomPlayerPath = string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString();
                    break;
                case "lyricsenabled" when IsBool(v):
                    c.LyricsEnabled = v.GetBoolean();
                    break;
                case "lyricsinpill" when IsBool(v):
                    c.LyricsInPill = v.GetBoolean();
                    break;
                case "pollintervalseconds" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var s):
                    c.PollIntervalSeconds = Math.Clamp(s, 5, 3600);
                    break;
                case "sections" when v.ValueKind == JsonValueKind.Array:
                    var list = new List<NotchSectionConfig>();
                    foreach (var item in v.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                            && item.TryGetProperty("enabled", out var en) && IsBool(en))
                        {
                            list.Add(new NotchSectionConfig { Id = id.GetString()!, Enabled = en.GetBoolean() });
                        }
                    }

                    c.Sections = NotchSections.Normalize(list);
                    break;
            }
        }

        return c;
    }

    private static bool IsBool(JsonElement v) => v.ValueKind is JsonValueKind.True or JsonValueKind.False;

    public static CentralChanges Diff(CentralSettings a, CentralSettings b)
    {
        var r = CentralChanges.None;
        if (a.Position != b.Position) r |= CentralChanges.Position;
        if (a.GlassLevel != b.GlassLevel) r |= CentralChanges.Glass;
        if (a.Sections.Count != b.Sections.Count || a.Sections.Zip(b.Sections).Any(p => p.First.Id != p.Second.Id || p.First.Enabled != p.Second.Enabled)) r |= CentralChanges.Sections;
        if (a.MusicEnabled != b.MusicEnabled) r |= CentralChanges.MusicModule;
        if (a.AgentsEnabled != b.AgentsEnabled) r |= CentralChanges.AgentsModule;
        if (a.MusicPlayer != b.MusicPlayer || a.CustomPlayerPath != b.CustomPlayerPath) r |= CentralChanges.Player;
        if (a.LyricsEnabled != b.LyricsEnabled || a.LyricsInPill != b.LyricsInPill) r |= CentralChanges.Lyrics;
        if (a.PollIntervalSeconds != b.PollIntervalSeconds) r |= CentralChanges.Poll;
        return r;
    }

    /// <summary>A patch with just the sections (tab order and visibility), as sent when the user reorders the tabs on the notch itself.</summary>
    public static object SectionsPatch(IEnumerable<NotchSectionConfig> sections) =>
        new { sections = sections.Select(s => new { id = s.Id, enabled = s.Enabled }).ToArray() };

    public static object GlassPatch(int level) => new { glassLevel = Math.Clamp(level, 0, 100) };
}
