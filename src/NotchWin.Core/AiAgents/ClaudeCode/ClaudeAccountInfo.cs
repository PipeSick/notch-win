using System.Text.Json;

namespace NotchWin.Core.AiAgents.ClaudeCode;

/// <summary>
/// The signed-in Claude account's e-mail and plan, read from the non-secret <c>oauthAccount</c> block of
/// <c>~/.claude.json</c>. Credentials and tokens are never read.
/// </summary>
public sealed record ClaudeAccountInfo(string? Email, string? Plan)
{
    public static ClaudeAccountInfo? TryRead(string claudeHome)
    {
        // ~/.claude.json sits next to the ~/.claude directory (or inside a custom home).
        var parent = Path.GetDirectoryName(claudeHome.TrimEnd('/', '\\'));
        foreach (var candidate in new[]
                 {
                     parent is null ? null : Path.Combine(parent, ".claude.json"),
                     Path.Combine(claudeHome, ".claude.json"),
                 })
        {
            if (candidate is null || !File.Exists(candidate))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(candidate));
                if (!doc.RootElement.TryGetProperty("oauthAccount", out var account) || account.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var email = ReadString(account, "emailAddress");
                var plan = PlanFromOrganizationType(ReadString(account, "organizationType"));
                return email is null && plan is null ? null : new ClaudeAccountInfo(email, plan);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // A half-written config file: just show the card without account details.
            }
        }

        return null;
    }

    public static string? PlanFromOrganizationType(string? organizationType) => organizationType?.ToLowerInvariant() switch
    {
        "claude_pro" => "Pro",
        "claude_max" => "Max",
        "claude_team" => "Team",
        "claude_enterprise" => "Enterprise",
        _ => null,
    };

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } s ? s : null;
}
