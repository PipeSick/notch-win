using NotchWin.Core.Music;

namespace NotchWin.Core.Settings;

public sealed class AppSettings
{
    public bool NotchModuleEnabled { get; set; } = true;
    public bool MusicModuleEnabled { get; set; } = true;
    public bool AiAgentsModuleEnabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>Override for the Claude Code home dir; null uses the default (~/.claude).</summary>
    public string? ClaudeHomeOverride { get; set; }

    /// <summary>Override for the Copilot CLI home dir; null uses the default (~/.copilot).</summary>
    public string? CopilotHomeOverride { get; set; }

    /// <summary>
    /// Extra Codex accounts to track, as CODEX_HOME directories. The default (~/.codex, or the
    /// process's own CODEX_HOME) is always included in addition to these.
    /// </summary>
    public List<string> AdditionalCodexHomes { get; set; } = [];

    /// <summary>
    /// Optional Anthropic Admin API key (sk-ant-admin...), used only to fetch organization-level
    /// Claude Code usage/rate-limit data. This is NOT the key for a personal Claude.ai Pro/Max plan —
    /// those don't expose a personal usage API, see docs/AI_AGENT_SOURCES.md. Stored locally only.
    /// </summary>
    public string? AnthropicAdminApiKey { get; set; }

    public NotchPosition NotchPosition { get; set; } = NotchPosition.TopCenter;

    /// <summary>Order and visibility of the notch's tabs; pass through <see cref="NotchSections.Normalize"/> before use.</summary>
    public List<NotchSectionConfig> NotchSections { get; set; } = Settings.NotchSections.Defaults();

    /// <summary>
    /// Path to an .ics file the calendar tab reads. Windows' own calendar store needs an app package
    /// identity this unpackaged app doesn't have, so a local .ics (e.g. synced from Google/Outlook) is the source.
    /// </summary>
    public string? CalendarIcsPath { get; set; }

    /// <summary>
    /// Show synced lyrics under the player. Off by default because it sends the track's title and
    /// artist to NetEase Music, the only thing NotchWin sends off the machine.
    /// </summary>
    public bool LyricsEnabled { get; set; }

    /// <summary>Show the current lyric line in the collapsed notch instead of the song title (needs <see cref="LyricsEnabled"/>).</summary>
    public bool LyricsInPill { get; set; }

    /// <summary>Override for OpenCode's data dir; null uses $XDG_DATA_HOME/opencode or ~/.local/share/opencode.</summary>
    public string? OpenCodeDataOverride { get; set; }

    /// <summary>Folder watched for in-progress browser downloads; null uses the user's Downloads folder.</summary>
    public string? DownloadsFolder { get; set; }

    /// <summary>Player opened by the notch's "Abrir …" button when nothing is playing; also preferred when several apps play.</summary>
    public MusicPlayerKind MusicPlayer { get; set; } = MusicPlayerKind.Spotify;

    /// <summary>Path to the player's .exe (or shortcut) when <see cref="MusicPlayer"/> is Custom; also used if Feishin isn't in a standard place.</summary>
    public string? CustomPlayerPath { get; set; }

    /// <summary>How opaque the notch's glass is, 0 (clear) to 100 (solid black like a hardware notch).</summary>
    public int GlassLevel { get; set; } = 60;
}

public enum NotchPosition
{
    TopCenter,
    TopLeft,
    TopRight,
}
