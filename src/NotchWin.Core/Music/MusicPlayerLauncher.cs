namespace NotchWin.Core.Music;

/// <summary>The player the notch opens when nothing is playing, and the one it prefers when several apps play at once.</summary>
public enum MusicPlayerKind
{
    Spotify,

    /// <summary>Feishin, a desktop client for Navidrome and other self-hosted music servers.</summary>
    Feishin,

    /// <summary>Any other app, given by the path to its .exe (or a shortcut).</summary>
    Custom,
}

/// <summary>What to start: a file path or a URI, opened through the shell so protocol links and shortcuts work.</summary>
public sealed record LaunchTarget(string FileName);

/// <summary>Works out how to open the chosen music player. UI-free so it can be unit tested.</summary>
public static class MusicPlayerLauncher
{
    public static string DisplayName(MusicPlayerKind kind, string? customPath) => kind switch
    {
        MusicPlayerKind.Spotify => "Spotify",
        MusicPlayerKind.Feishin => "Feishin",
        _ => string.IsNullOrWhiteSpace(customPath)
            ? "reproductor"
            : WindowsFileName(customPath.Trim()),
    };

    /// <summary>
    /// Lowercase text found in the media session id of that player's app (e.g. "SpotifyAB.SpotifyMusic_…!Spotify"),
    /// used to prefer its session over other apps that are playing. Null when it can't be known.
    /// </summary>
    public static string? SessionToken(MusicPlayerKind kind, string? customPath) => kind switch
    {
        MusicPlayerKind.Spotify => "spotify",
        MusicPlayerKind.Feishin => "feishin",
        _ => string.IsNullOrWhiteSpace(customPath)
            ? null
            : WindowsFileName(customPath.Trim()).ToLowerInvariant(),
    };

    /// <summary>The target to start, or null if the player can't be found (not installed, or the custom path is wrong).</summary>
    public static LaunchTarget? Resolve(
        MusicPlayerKind kind, string? customPath, Func<string, bool> fileExists, Func<string, string?> getEnv)
    {
        switch (kind)
        {
            case MusicPlayerKind.Spotify:
                // The spotify: protocol works for both the Microsoft Store and the classic installer.
                return new LaunchTarget("spotify:");

            case MusicPlayerKind.Feishin:
                var found = FeishinCandidates(getEnv).FirstOrDefault(fileExists);
                if (found is null && customPath?.Trim().Trim('"') is { Length: > 0 } custom && fileExists(custom))
                {
                    found = custom; // installed somewhere non-standard and the user pointed us at it
                }

                return found is null ? null : new LaunchTarget(found);

            default:
                var path = customPath?.Trim().Trim('"');
                return !string.IsNullOrEmpty(path) && fileExists(path) ? new LaunchTarget(path) : null;
        }
    }

    // These are Windows paths ("C:\Users\..."): handle them as such wherever the code runs, instead of with the host OS rules of System.IO.Path
    // (which on Linux do not know "\\" and made the unit tests depend on the platform). On Windows the results are the same.
    private static string Join(string root, params string[] parts) => string.Join('\\', new[] { root.TrimEnd('\\', '/') }.Concat(parts));

    private static string WindowsFileName(string path)
    {
        var name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    private static IEnumerable<string> FeishinCandidates(Func<string, string?> getEnv)
    {
        var local = getEnv("LOCALAPPDATA");
        var programFiles = getEnv("ProgramFiles");
        var roaming = getEnv("APPDATA");

        if (!string.IsNullOrEmpty(local))
        {
            yield return Join(local, "Programs", "feishin", "Feishin.exe");
            yield return Join(local, "Programs", "Feishin", "Feishin.exe");
            yield return Join(local, "feishin", "Feishin.exe");
        }

        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Join(programFiles, "Feishin", "Feishin.exe");
        }

        if (!string.IsNullOrEmpty(roaming))
        {
            yield return Join(roaming, "Microsoft", "Windows", "Start Menu", "Programs", "Feishin.lnk");
        }
    }
}
