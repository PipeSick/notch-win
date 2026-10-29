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
}

public enum NotchPosition
{
    TopCenter,
    TopLeft,
    TopRight,
}
