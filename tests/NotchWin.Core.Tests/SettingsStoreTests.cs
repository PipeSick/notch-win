using NotchWin.Core.Settings;
using Xunit;

namespace NotchWin.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), "notchwin-tests-settings-" + Guid.NewGuid() + ".json");

    public void Dispose()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var store = new SettingsStore(_filePath);

        var settings = store.Load();

        Assert.True(settings.NotchModuleEnabled);
        Assert.True(settings.MusicModuleEnabled);
        Assert.True(settings.AiAgentsModuleEnabled);
        Assert.Empty(settings.AdditionalCodexHomes);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = new SettingsStore(_filePath);
        var original = new AppSettings
        {
            NotchModuleEnabled = false,
            PollIntervalSeconds = 45,
            ClaudeHomeOverride = "/custom/claude",
            AdditionalCodexHomes = ["/codex/account-b"],
            AnthropicAdminApiKey = "sk-ant-admin-fake",
            NotchPosition = NotchPosition.TopLeft,
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.False(loaded.NotchModuleEnabled);
        Assert.Equal(45, loaded.PollIntervalSeconds);
        Assert.Equal("/custom/claude", loaded.ClaudeHomeOverride);
        Assert.Equal(["/codex/account-b"], loaded.AdditionalCodexHomes);
        Assert.Equal("sk-ant-admin-fake", loaded.AnthropicAdminApiKey);
        Assert.Equal(NotchPosition.TopLeft, loaded.NotchPosition);
    }

    [Fact]
    public void Save_CreatesParentDirectory_WhenMissing()
    {
        var nestedPath = Path.Combine(Path.GetTempPath(), "notchwin-tests-nested-" + Guid.NewGuid(), "settings.json");
        var store = new SettingsStore(nestedPath);

        store.Save(new AppSettings());

        Assert.True(File.Exists(nestedPath));
        Directory.Delete(Path.GetDirectoryName(nestedPath)!, recursive: true);
    }
}
