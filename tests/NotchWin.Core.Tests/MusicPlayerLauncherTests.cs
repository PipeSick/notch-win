using NotchWin.Core.Music;

namespace NotchWin.Core.Tests;

public sealed class MusicPlayerLauncherTests
{
    private static string? Env(string name) => name switch
    {
        "LOCALAPPDATA" => @"C:\Users\x\AppData\Local",
        "ProgramFiles" => @"C:\Program Files",
        "APPDATA" => @"C:\Users\x\AppData\Roaming",
        _ => null,
    };

    [Fact]
    public void Spotify_UsesTheProtocolLink_EvenWithoutFileChecks()
    {
        var target = MusicPlayerLauncher.Resolve(MusicPlayerKind.Spotify, null, _ => false, Env);

        Assert.Equal("spotify:", target!.FileName);
    }

    [Fact]
    public void Feishin_FindsTheFirstExistingStandardLocation()
    {
        var wanted = @"C:\Users\x\AppData\Local\Programs\feishin\Feishin.exe";

        var target = MusicPlayerLauncher.Resolve(MusicPlayerKind.Feishin, null, p => p == wanted, Env);

        Assert.Equal(wanted, target!.FileName);
    }

    [Fact]
    public void Feishin_NotInstalled_ReturnsNull()
    {
        Assert.Null(MusicPlayerLauncher.Resolve(MusicPlayerKind.Feishin, null, _ => false, Env));
    }

    [Fact]
    public void Feishin_FallsBackToTheCustomPath_WhenNotInAStandardPlace()
    {
        var custom = @"D:\Apps\Feishin\Feishin.exe";

        var target = MusicPlayerLauncher.Resolve(MusicPlayerKind.Feishin, $"\"{custom}\"", p => p == custom, Env);

        Assert.Equal(custom, target!.FileName);
    }

    [Fact]
    public void Custom_RequiresAnExistingPath()
    {
        Assert.Null(MusicPlayerLauncher.Resolve(MusicPlayerKind.Custom, null, _ => true, Env));
        Assert.Null(MusicPlayerLauncher.Resolve(MusicPlayerKind.Custom, @"C:\nope.exe", _ => false, Env));
        Assert.Equal(@"C:\ok.exe", MusicPlayerLauncher.Resolve(MusicPlayerKind.Custom, @"C:\ok.exe", _ => true, Env)!.FileName);
    }

    [Fact]
    public void NamesAndSessionTokens()
    {
        Assert.Equal("Spotify", MusicPlayerLauncher.DisplayName(MusicPlayerKind.Spotify, null));
        Assert.Equal("Feishin", MusicPlayerLauncher.DisplayName(MusicPlayerKind.Feishin, null));
        Assert.Equal("Strawberry", MusicPlayerLauncher.DisplayName(MusicPlayerKind.Custom, @"C:\Apps\Strawberry.exe"));
        Assert.Equal("reproductor", MusicPlayerLauncher.DisplayName(MusicPlayerKind.Custom, null));

        Assert.Equal("spotify", MusicPlayerLauncher.SessionToken(MusicPlayerKind.Spotify, null));
        Assert.Equal("feishin", MusicPlayerLauncher.SessionToken(MusicPlayerKind.Feishin, null));
        Assert.Equal("strawberry", MusicPlayerLauncher.SessionToken(MusicPlayerKind.Custom, @"C:\Apps\Strawberry.exe"));
        Assert.Null(MusicPlayerLauncher.SessionToken(MusicPlayerKind.Custom, null));
    }
}
