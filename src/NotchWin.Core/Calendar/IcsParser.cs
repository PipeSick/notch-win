using System.Globalization;
using System.Text;

namespace NotchWin.Core.Calendar;

public sealed record CalendarEvent(string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay);

/// <summary>
/// Minimal iCalendar reader for the notch's "today" list: SUMMARY/DTSTART/DTEND of each VEVENT.
/// Known limits, on purpose: no RRULE expansion (a recurring event shows only on its first date) and
/// TZID-qualified times are read as local time rather than converted.
/// </summary>
public static class IcsParser
{
    public static List<CalendarEvent> Parse(string text)
    {
        var events = new List<CalendarEvent>();
        string? title = null, startRaw = null, endRaw = null, startKey = null;
        var inEvent = false;

        foreach (var line in Unfold(text))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                inEvent = true;
                title = startRaw = endRaw = startKey = null;
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                inEvent = false;
                if (startRaw is not null && TryParseTime(startKey!, startRaw, out var start, out var allDay))
                {
                    var end = endRaw is not null && TryParseTime(startKey!, endRaw, out var e, out _)
                        ? e
                        : (allDay ? start.AddDays(1) : start);
                    events.Add(new CalendarEvent(string.IsNullOrWhiteSpace(title) ? "(sin título)" : title, start, end, allDay));
                }

                continue;
            }

            if (!inEvent)
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var name = line[..colon];
            var value = line[(colon + 1)..];
            var baseName = name.Split(';')[0].ToUpperInvariant();

            switch (baseName)
            {
                case "SUMMARY":
                    title = Unescape(value);
                    break;
                case "DTSTART":
                    startRaw = value;
                    startKey = name;
                    break;
                case "DTEND":
                    endRaw = value;
                    break;
            }
        }

        return events.OrderBy(e => e.Start).ToList();
    }

    /// <summary>Events that overlap the local day containing <paramref name="now"/>, and haven't finished yet.</summary>
    public static List<CalendarEvent> UpcomingToday(IEnumerable<CalendarEvent> events, DateTimeOffset now)
    {
        var dayStart = new DateTimeOffset(now.Date, now.Offset);
        var dayEnd = dayStart.AddDays(1);
        return events
            .Where(e => e.Start < dayEnd && e.End > dayStart && e.End > now)
            .OrderBy(e => e.Start)
            .ToList();
    }

    private static IEnumerable<string> Unfold(string text)
    {
        // RFC 5545: a line starting with a space or tab continues the previous one.
        var current = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                current.Append(line, 1, line.Length - 1);
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }

            current.Append(line);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static bool TryParseTime(string key, string value, out DateTimeOffset result, out bool allDay)
    {
        result = default;
        allDay = key.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) && !key.Contains("DATE-TIME", StringComparison.OrdinalIgnoreCase)
                 || value.Length == 8;

        if (allDay)
        {
            if (!DateTime.TryParseExact(value[..Math.Min(8, value.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                return false;
            }

            result = new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day));
            return true;
        }

        if (value.EndsWith('Z'))
        {
            if (!DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
            {
                return false;
            }

            result = new DateTimeOffset(utc, TimeSpan.Zero).ToLocalTime();
            return true;
        }

        if (!DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return false;
        }

        result = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        return true;
    }

    private static string Unescape(string value) =>
        value.Replace("\\n", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("\\,", ",")
            .Replace("\\;", ";")
            .Replace("\\\\", "\\");
}
