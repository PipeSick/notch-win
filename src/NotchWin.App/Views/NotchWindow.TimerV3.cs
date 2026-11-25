using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotchWin.App.Views.V3;
using NotchWin.Core.Notch.V3;

namespace NotchWin.App.Views;

/// <summary>
/// Pestaña Temporizador V3: anillo con el tiempo, estado en palabras, presets (5, 15, 25, 50 y 90 min) y botones que cambian según el estado REAL del temporizador
/// (<see cref="TimerView"/>). La estructura se arma una vez y cada segundo solo se actualiza el anillo y el tiempo; los botones se vuelven a armar únicamente al cambiar de fase.
/// Reiniciar = tiempo completo y sigue corriendo; Cancelar = descarta el temporizador. La cápsula y Inicio leen el mismo estado, así que no pueden contradecirse.
/// </summary>
public sealed partial class NotchWindow
{
    private Microsoft.UI.Xaml.Controls.ProgressRing? _tRing;
    private TextBlock? _tTime, _tLabel;
    private StackPanel? _tButtons, _tPresets;
    private TimerPhase? _tPhase;
    private int _tPresetBuilt = -1;

    private void BuildTimerV3()
    {
        TimerPane.Children.Clear();
        var stage = new Grid { Width = 168, Height = 168, HorizontalAlignment = HorizontalAlignment.Center };
        _tRing = new Microsoft.UI.Xaml.Controls.ProgressRing { IsIndeterminate = false, Minimum = 0, Maximum = 100, Value = 100, Width = 168, Height = 168, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(V3Ui.Accent), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) };
        stage.Children.Add(_tRing);
        var centre = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
        _tTime = new TextBlock { FontSize = 36, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        _tLabel = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
        centre.Children.Add(_tTime); centre.Children.Add(_tLabel); stage.Children.Add(centre);
        TimerPane.Children.Add(stage);
        _tPresets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        _tButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center };
        TimerPane.Children.Add(_tPresets); TimerPane.Children.Add(_tButtons);
        _tPhase = null; _tPresetBuilt = -1;
    }

    private void UpdateTimerV3(TimerPhase phase)
    {
        if (_tRing is null || _tTime is null) BuildTimerV3();
        var t = _viewModel.Timer;
        _tTime!.Text = FormatCountdown(t.Remaining); _tTime.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Pal.Fg);
        _tLabel!.Text = TimerView.Label(phase); _tLabel.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(CardUi.Dim);
        _tRing!.Value = (phase == TimerPhase.Ready ? 1 : t.FractionLeft) * 100;

        int minutes = (int)Math.Round(t.Total.TotalMinutes);
        if (_tPhase != phase || _tPresetBuilt != minutes)
        {
            _tPhase = phase; _tPresetBuilt = minutes;
            _tButtons!.Children.Clear();
            foreach (var a in TimerView.Actions(phase))
            {
                var (glyph, style) = a switch { TimerAction.Start or TimerAction.Resume => ("", ActionStyle.Primary), TimerAction.Pause => ("", ActionStyle.Primary), TimerAction.Restart => ("", ActionStyle.Secondary), _ => ("", ActionStyle.Secondary) };
                var act = a; _tButtons.Children.Add(V3Ui.ActionButton(glyph, TimerView.ActionLabel(a, phase), () => ApplyTimerAction(act), style, size: a is TimerAction.Start or TimerAction.Resume or TimerAction.Pause ? 52 : 40));
            }

            _tPresets!.Children.Clear();
            var enabled = TimerView.PresetsEnabled(phase);
            foreach (var m in TimerView.PresetMinutes)
            {
                int mins = m; var b = V3Ui.TextButton(mins + " min", () => { _viewModel.Timer.SetDuration(TimeSpan.FromMinutes(mins)); UpdateTimerUi(); RebuildHome(force: true); }, mins == minutes ? ActionStyle.Primary : ActionStyle.Secondary, enabled);
                _tPresets.Children.Add(b);
            }
        }
    }
}
