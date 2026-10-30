namespace NotchWin.Core.Settings;

/// <summary>One tab of the expanded notch: which section it is and whether the user wants it shown.</summary>
public sealed class NotchSectionConfig
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}

/// <summary>The known notch sections and the default order/visibility, plus merging of a saved layout with them.</summary>
public static class NotchSections
{
    public const string Music = "music";
    public const string Agents = "agents";
    public const string Timer = "timer";
    public const string Calendar = "calendar";
    public const string Downloads = "downloads";
    public const string Notifications = "notifications";

    public static IReadOnlyList<string> AllIds { get; } =
        [Music, Agents, Timer, Calendar, Downloads, Notifications];

    public static List<NotchSectionConfig> Defaults() =>
        AllIds.Select(id => new NotchSectionConfig { Id = id, Enabled = true }).ToList();

    /// <summary>
    /// Keeps the saved order and on/off state, drops ids this build doesn't know (a section removed
    /// in a later version) and appends any known id the saved layout lacks (a section added later),
    /// so upgrading never loses a tab or leaves the layout empty.
    /// </summary>
    public static List<NotchSectionConfig> Normalize(IEnumerable<NotchSectionConfig>? saved)
    {
        var result = new List<NotchSectionConfig>();
        var seen = new HashSet<string>();

        foreach (var item in saved ?? [])
        {
            if (AllIds.Contains(item.Id) && seen.Add(item.Id))
            {
                result.Add(new NotchSectionConfig { Id = item.Id, Enabled = item.Enabled });
            }
        }

        foreach (var id in AllIds.Where(id => !seen.Contains(id)))
        {
            result.Add(new NotchSectionConfig { Id = id, Enabled = true });
        }

        return result;
    }
}
