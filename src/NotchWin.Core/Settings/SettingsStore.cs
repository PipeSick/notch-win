using System.Text.Json;

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

    /// <summary>Default location: %LOCALAPPDATA%\NotchWin\settings.json (falls back to user profile elsewhere).</summary>
    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "NotchWin", "settings.json");
    }
}
