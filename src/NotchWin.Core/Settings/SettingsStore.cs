using System.Text.Json;
using System.Text.Json.Nodes;

namespace NotchWin.Core.Settings;

/// <summary>Loads/saves <see cref="AppSettings"/> as indented JSON at a caller-supplied path.</summary>
public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; } = filePath;

    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(FilePath, json);
    }

    /// <summary>
    /// Settings files written by older versions may hold a plaintext Anthropic admin key. NotchWin never uses it and must not keep it: remove it from the file in place
    /// (everything else is left as it was). Returns true when something was removed. A file that is missing or unreadable is left alone.
    /// </summary>
    public bool RemoveLegacySecrets()
    {
        try
        {
            if (!File.Exists(FilePath) || JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject root)
            {
                return false;
            }

            var found = root.Where(kv => LegacySecretKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            if (found.Count == 0)
            {
                return false;
            }

            foreach (var key in found)
            {
                root.Remove(key);
            }

            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static readonly string[] LegacySecretKeys = ["AnthropicAdminApiKey"];

    /// <summary>Default location: %LOCALAPPDATA%\NotchWin\settings.json (falls back to user profile elsewhere).</summary>
    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "NotchWin", "settings.json");
    }
}
