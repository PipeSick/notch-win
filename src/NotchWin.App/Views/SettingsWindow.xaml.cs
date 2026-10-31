using Microsoft.UI.Xaml;
using NotchWin.Core.Music;
using NotchWin.Core.Settings;

namespace NotchWin.App.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;

    public event Action<AppSettings>? SettingsSaved;

    public SettingsWindow(SettingsStore store)
    {
        _store = store;
        InitializeComponent();
        LoadIntoControls(_store.Load());
    }

    private void LoadIntoControls(AppSettings settings)
    {
        NotchModuleCheckBox.IsChecked = settings.NotchModuleEnabled;
        MusicModuleCheckBox.IsChecked = settings.MusicModuleEnabled;
        AiAgentsModuleCheckBox.IsChecked = settings.AiAgentsModuleEnabled;
        PlayerCombo.SelectedIndex = (int)settings.MusicPlayer;
        PlayerPathBox.Text = settings.CustomPlayerPath ?? string.Empty;
        PollIntervalBox.Value = settings.PollIntervalSeconds;
        ClaudeHomeBox.Text = settings.ClaudeHomeOverride ?? string.Empty;
        CopilotHomeBox.Text = settings.CopilotHomeOverride ?? string.Empty;
        CodexHomesBox.Text = string.Join(Environment.NewLine, settings.AdditionalCodexHomes);
        AnthropicAdminKeyBox.Password = settings.AnthropicAdminApiKey ?? string.Empty;
        LyricsCheckBox.IsChecked = settings.LyricsEnabled;
        LyricsInPillCheckBox.IsChecked = settings.LyricsInPill;
        OpenCodeBox.Text = settings.OpenCodeDataOverride ?? string.Empty;
        CalendarIcsBox.Text = settings.CalendarIcsPath ?? string.Empty;
        DownloadsFolderBox.Text = settings.DownloadsFolder ?? string.Empty;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        // Start from what's on disk so fields this window doesn't edit (tab order, position) survive a save.
        var settings = _store.Load();
        settings.NotchModuleEnabled = NotchModuleCheckBox.IsChecked ?? true;
        settings.MusicModuleEnabled = MusicModuleCheckBox.IsChecked ?? true;
        settings.AiAgentsModuleEnabled = AiAgentsModuleCheckBox.IsChecked ?? true;
        settings.MusicPlayer = (MusicPlayerKind)Math.Max(0, PlayerCombo.SelectedIndex);
        settings.CustomPlayerPath = string.IsNullOrWhiteSpace(PlayerPathBox.Text) ? null : PlayerPathBox.Text.Trim().Trim('"');
        settings.PollIntervalSeconds = double.IsNaN(PollIntervalBox.Value) ? 30 : (int)PollIntervalBox.Value;
        settings.ClaudeHomeOverride = string.IsNullOrWhiteSpace(ClaudeHomeBox.Text) ? null : ClaudeHomeBox.Text;
        settings.CopilotHomeOverride = string.IsNullOrWhiteSpace(CopilotHomeBox.Text) ? null : CopilotHomeBox.Text;
        settings.AdditionalCodexHomes = CodexHomesBox.Text
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        settings.AnthropicAdminApiKey = string.IsNullOrWhiteSpace(AnthropicAdminKeyBox.Password) ? null : AnthropicAdminKeyBox.Password;
        settings.LyricsEnabled = LyricsCheckBox.IsChecked ?? false;
        settings.LyricsInPill = LyricsInPillCheckBox.IsChecked ?? false;
        settings.OpenCodeDataOverride = string.IsNullOrWhiteSpace(OpenCodeBox.Text) ? null : OpenCodeBox.Text.Trim();
        settings.CalendarIcsPath = string.IsNullOrWhiteSpace(CalendarIcsBox.Text) ? null : CalendarIcsBox.Text.Trim();
        settings.DownloadsFolder = string.IsNullOrWhiteSpace(DownloadsFolderBox.Text) ? null : DownloadsFolderBox.Text.Trim();

        _store.Save(settings);
        SettingsSaved?.Invoke(settings);
        Close();
    }
}
