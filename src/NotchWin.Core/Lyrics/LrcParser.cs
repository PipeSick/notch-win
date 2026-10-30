using System.Globalization;
using System.Text.RegularExpressions;

namespace NotchWin.Core.Lyrics;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Parses synced lyrics in LRC format: <c>[mm:ss.xx] text</c>, with optional <c>[offset:±ms]</c>.</summary>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    public static List<LyricLine> Parse(string lrc)
    {
        var lines = new List<LyricLine>();
        var offsetMs = 0;

        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.Trim();
            var offset = OffsetTag().Match(line);
            if (offset.Success)
            {
                offsetMs = int.Parse(offset.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }

            var tags = TimeTag().Matches(line);
            if (tags.Count == 0)
            {
                continue;
            }

            var text = TimeTag().Replace(line, string.Empty).Trim();
            foreach (Match tag in tags)
            {
                var minutes = int.Parse(tag.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(tag.Groups[2].Value, CultureInfo.InvariantCulture);
                // ".5" = 500 ms, ".50" = 500 ms, ".05" = 50 ms, ".123" = 123 ms.
                var ms = tag.Groups[3].Success
                    ? int.Parse(tag.Groups[3].Value.PadRight(3, '0'), CultureInfo.InvariantCulture)
                    : 0;
                var time = new TimeSpan(0, 0, minutes, seconds, ms) - TimeSpan.FromMilliseconds(offsetMs);
                lines.Add(new LyricLine(time < TimeSpan.Zero ? TimeSpan.Zero : time, text));
            }
        }

        return lines.OrderBy(l => l.Time).ToList();
    }

    /// <summary>Index of the line being sung at <paramref name="position"/>, or -1 before the first line.</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lyrics, TimeSpan position)
    {
        var index = -1;
        for (var i = 0; i < lyrics.Count && lyrics[i].Time <= position; i++)
        {
            index = i;
        }

        return index;
    }
}
