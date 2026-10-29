using System.Text.Json;

namespace NotchWin.Core.AiAgents;

/// <summary>Small IO/JSON helpers shared by the per-provider JSONL readers, all of which must
/// tolerate missing files, locked files, and malformed lines without throwing.</summary>
internal static class JsonlFileHelpers
{
    public static DateTimeOffset SafeGetLastWriteTimeUtc(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (IOException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    public static IEnumerable<string> ReadLinesSafely(string path)
    {
        try
        {
            return File.ReadLines(path);
        }
        catch (IOException)
        {
            return [];
        }
    }

    public static bool TryParseJsonLine(string line, out JsonDocument document)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            document = null!;
            return false;
        }

        try
        {
            document = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }
}
