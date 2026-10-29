using Microsoft.UI.Xaml;
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
        PollIntervalBox.Value = settings.PollIntervalSeconds;
        ClaudeHomeBox.Text = settings.ClaudeHomeOverride ?? string.Empty;
        CopilotHomeBox.Text = settings.CopilotHomeOverride ?? string.Empty;
        CodexHomesBox.Text = string.Join(Environment.NewLine, settings.AdditionalCodexHomes);
        AnthropicAdminKeyBox.Password = settings.AnthropicAdminApiKey ?? string.Empty;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            NotchModuleEnabled = NotchModuleCheckBox.IsChecked ?? true,
            MusicModuleEnabled = MusicModuleCheckBox.IsChecked ?? true,
            AiAgentsModuleEnabled = AiAgentsModuleCheckBox.IsChecked ?? true,
            PollIntervalSeconds = double.IsNaN(PollIntervalBox.Value) ? 30 : (int)PollIntervalBox.Value,
            ClaudeHomeOverride = string.IsNullOrWhiteSpace(ClaudeHomeBox.Text) ? null : ClaudeHomeBox.Text,
            CopilotHomeOverride = string.IsNullOrWhiteSpace(CopilotHomeBox.Text) ? null : CopilotHomeBox.Text,
            AdditionalCodexHomes = CodexHomesBox.Text
                .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            AnthropicAdminApiKey = string.IsNullOrWhiteSpace(AnthropicAdminKeyBox.Password) ? null : AnthropicAdminKeyBox.Password,
        };

        _store.Save(settings);
        SettingsSaved?.Invoke(settings);
        Close();
    }
}
