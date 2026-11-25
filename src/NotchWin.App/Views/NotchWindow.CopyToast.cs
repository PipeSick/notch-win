using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.App.Views.V3;

namespace NotchWin.App.Views;

/// <summary>Confirmación «Copiado al portapapeles»: dentro del Notch abierto es un aviso breve abajo; con el Notch cerrado sale como banner de la píldora.</summary>
public sealed partial class NotchWindow
{
    private Border? _copyToast;
    private TextBlock? _copyToastText;
    private DispatcherQueueTimer? _copyToastTimer;

    private void ShowCopied(string text = "Copiado al portapapeles")
    {
        try
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_closed) return;
                if (!_expanded)
                {
                    _pill.Post(new NotchWin.Core.Pill.PillItem(NotchWin.Core.Pill.PillKind.Activity, text, "Portapapeles") { App = "Portapapeles" });
                    return;
                }

                if (_copyToast is null)
                {
                    _copyToastText = CardUi.Text(text, 12.5, Pal.Fg, bold: true);
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(CardUi.Icon("", 14, V3Ui.AccentText));
                    row.Children.Add(_copyToastText);
                    _copyToast = V3Ui.Surface(row, 9);
                    _copyToast.HorizontalAlignment = HorizontalAlignment.Center; _copyToast.VerticalAlignment = VerticalAlignment.Bottom;
                    _copyToast.Margin = new Thickness(0, 0, 0, 14); _copyToast.IsHitTestVisible = false;
                    Grid.SetRowSpan(_copyToast, 99); Grid.SetColumnSpan(_copyToast, 99);
                    Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(_copyToast, 100);
                    RootGrid.Children.Add(_copyToast);
                }

                _copyToastText!.Text = text; _copyToast.Visibility = Visibility.Visible;
                _copyToastTimer ??= DispatcherQueue.CreateTimer();
                _copyToastTimer.Stop(); _copyToastTimer.Interval = TimeSpan.FromMilliseconds(1600); _copyToastTimer.IsRepeating = false;
                _copyToastTimer.Tick -= HideCopyToast; _copyToastTimer.Tick += HideCopyToast; _copyToastTimer.Start();
            });
        }
        catch (Exception)
        {
            // el aviso es cortesía: nunca rompe la copia
        }
    }

    private void HideCopyToast(DispatcherQueueTimer sender, object args) { sender.Stop(); if (_copyToast is not null) _copyToast.Visibility = Visibility.Collapsed; }
}
