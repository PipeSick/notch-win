using System.Globalization;
using System.Text.Json;

namespace NotchWin.Core.Usage;

/// <summary>
/// Daily spend (estimated API-value in USD, all agents together) kept on disk so the agents tab can draw a trend.
/// The agents' own logs only give "today", so the history starts the day NotchWin first runs and grows from there.
/// </summary>
public sealed class UsageHistory
{
    private const int KeepDays = 90;

    private readonly string _path;
    private readonly SortedDictionary<DateOnly, decimal> _days = [];

    public UsageHistory(string path)
    {
        _path = path;
        Load();
    }

    /// <summary>Records <paramref name="costUsd"/> for a day. A day's spend only grows, so the largest value seen wins.</summary>
    public void Record(DateOnly day, decimal costUsd)
    {
        if (costUsd <= 0 || (_days.TryGetValue(day, out var existing) && existing >= costUsd))
        {
            return;
        }

        _days[day] = costUsd;
        foreach (var old in _days.Keys.Where(d => d < day.AddDays(-KeepDays)).ToList())
        {
            _days.Remove(old);
        }

        Save();
    }

    /// <summary>The last <paramref name="days"/> days ending at <paramref name="today"/>, oldest first, zero where nothing was recorded.</summary>
    public IReadOnlyList<(DateOnly Day, decimal Cost)> Last(DateOnly today, int days) =>
        Enumerable.Range(0, days)
            .Select(i => today.AddDays(i - (days - 1)))
            .Select(d => (d, _days.GetValueOrDefault(d)))
            .ToList();

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var raw = JsonSerializer.Deserialize<Dictionary<string, decimal>>(File.ReadAllText(_path));
            foreach (var (key, value) in raw ?? [])
            {
                if (DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    _days[day] = value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable history just starts over.
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            var raw = _days.ToDictionary(p => p.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p => p.Value);
            File.WriteAllText(_path, JsonSerializer.Serialize(raw));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a day of trend data isn't worth surfacing.
        }
    }
}
