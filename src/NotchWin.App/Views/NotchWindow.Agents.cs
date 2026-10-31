using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchWin.Core.Models;
using Windows.UI;

namespace NotchWin.App.Views;

/// <summary>The agents tab: one card per agent, then today's spend, what each agent did last, and a daily trend.</summary>
public sealed partial class NotchWindow
{
    private static readonly GridLength Star = new(1, GridUnitType.Star);

    private void RebuildAgents()
    {
        AgentsList.Children.Clear();
        // Only agents that actually exist on this PC: a provider with no data at all would show an empty card.
        var visible = _viewModel.AgentSnapshots
            .Where(s => s.Error is null && (s.LastActivityUtc is not null || s.TokensToday.Total > 0 || s.PlanLimits.Count > 0))
            .ToList();
        AgentsEmpty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AgentsPeriod.Visibility = visible.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (visible.Count == 0)
        {
            return;
        }

        AgentsList.Children.Add(TwoColumns(visible.Select(BuildAgentCard).ToList()));
        AgentsList.Children.Add(TwoColumns([BuildSpendCard(visible), BuildNowCard(visible)]));
        AgentsList.Children.Add(BuildTrendCard(visible));
    }

    private static Grid TwoColumns(IReadOnlyList<UIElement> cards)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
        for (var i = 0; i < cards.Count; i++)
        {
            if (i % 2 == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            Grid.SetColumn((FrameworkElement)cards[i], i % 2);
            Grid.SetRow((FrameworkElement)cards[i], i / 2);
            grid.Children.Add(cards[i]);
        }

        return grid;
    }

    // ---------- agent card ----------

    private static UIElement BuildAgentCard(AgentUsageSnapshot s)
    {
        var accent = AccentFor(s.Kind);
        var body = new StackPanel { Spacing = 8 };

        var head = CardUi.Columns(GridLength.Auto, Star, GridLength.Auto);
        var initial = CardUi.Text(ShortName(s.Kind)[..1], 11, Colors.White, bold: true);
        initial.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = CardUi.Brush(accent), Child = initial,
        });

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(CardUi.Text(ShortName(s.Kind), 13, Colors.White, bold: true));
        if (s.AccountEmail is { Length: > 0 } email)
        {
            var mail = CardUi.Text("· " + email, 12, CardUi.Dim, trim: true);
            mail.MaxWidth = 120;
            title.Children.Add(mail);
        }

        head.Children.Add(CardUi.At(title, 1));
        if (s.PlanName is { Length: > 0 } plan)
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 2, 7, 2),
                Background = CardUi.Brush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)),
                Child = CardUi.Text(plan, 11, Lighten(accent), bold: true),
            };
            head.Children.Add(CardUi.At(badge, 2));
        }

        body.Children.Add(head);

        if (s.PlanLimits.Count > 0)
        {
            for (var i = 0; i < Math.Min(2, s.PlanLimits.Count); i++)
            {
                body.Children.Add(BuildLimit(s.PlanLimits[i], i, accent));
            }
        }
        else
        {
            // Claude's personal plan usage isn't exposed anywhere on disk, so show what is: today's volume.
            body.Children.Add(CardUi.Text(
                s.IsLiveWorking ? "Trabajando ahora" : s.ActiveProject ?? (s.Models.Count > 0 ? s.Models[0] : "Sin actividad"),
                12, CardUi.Dim, trim: true));
            body.Children.Add(CardUi.Text(FormatTokensShort(s.TokensToday.Total) + " tokens hoy", 15, Colors.White, bold: true));
            var note = CardUi.Text("El uso del plan no se puede leer en este equipo", 10, CardUi.Faint);
            note.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(note);
        }

        return CardUi.Card(body);
    }

    private static UIElement BuildLimit(PlanLimitInfo limit, int index, Color accent)
    {
        var remaining = Math.Clamp(100 - limit.UsedPercent, 0, 100);
        var isSession = limit.WindowMinutes is { } w ? w <= 360 : index == 0;

        double? elapsed = null;
        string? reset = null;
        if (limit.ResetsAt is { } at)
        {
            var left = at - DateTimeOffset.UtcNow;
            if (left < TimeSpan.Zero)
            {
                left = TimeSpan.Zero;
            }

            reset = FormatResetIn(left);
            if (limit.WindowMinutes is { } window and > 0)
            {
                elapsed = 1 - left.TotalMinutes / window;
            }
        }

        var panel = new StackPanel { Spacing = 5 };
        var row = CardUi.Columns(GridLength.Auto, Star, GridLength.Auto);
        row.ColumnSpacing = 5;
        row.Children.Add(CardUi.Text(isSession ? "Sesión" : "Semana", 12, Colors.White, bold: true));
        if (reset is not null)
        {
            var clock = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            clock.Children.Add(CardUi.Icon("", 10, CardUi.Dim));
            clock.Children.Add(CardUi.Text(reset, 11, CardUi.Dim));
            row.Children.Add(CardUi.At(clock, 1));
        }

        var pct = CardUi.Text($"{remaining:0}% restante", 12, Colors.White, bold: true);
        row.Children.Add(CardUi.At(pct, 2));
        panel.Children.Add(row);
        panel.Children.Add(CardUi.Bar(remaining / 100, remaining < 15 ? CardUi.Danger : accent, elapsed));
        return panel;
    }

    // ---------- spend / now / trend ----------

    private UIElement BuildSpendCard(IReadOnlyList<AgentUsageSnapshot> snapshots)
    {
        var total = snapshots.Sum(s => s.EstimatedCostUsdToday ?? 0);
        var priced = snapshots.Any(s => s.EstimatedCostUsdToday is not null);
        var tokens = snapshots.Sum(s => s.TokensToday.Total);
        var cacheRead = snapshots.Sum(s => s.TokensToday.CacheRead);

        var body = new StackPanel { Spacing = 6 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(CardUi.Icon("", 12, CardUi.Dim));
        head.Children.Add(CardUi.Text("Gasto", 13, Colors.White, bold: true));
        body.Children.Add(head);

        var amount = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var anyEstimate = snapshots.Any(s => s.IsEstimate);
        amount.Children.Add(CardUi.Text(priced ? (anyEstimate ? "~" : string.Empty) + Money(total) : "—", 26, Colors.White, bold: true));
        var unit = CardUi.Text(priced ? "valor de API" : "sin precios del modelo", 11, CardUi.Dim);
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        unit.Margin = new Thickness(0, 0, 0, 5);
        amount.Children.Add(unit);
        body.Children.Add(amount);

        body.Children.Add(SegmentedBar(snapshots, total));
        var cachePct = tokens > 0 ? cacheRead * 100.0 / tokens : 0;
        body.Children.Add(CardUi.Text($"{FormatTokensShort(tokens)} tokens · {cachePct:0}% desde la caché", 11, CardUi.Dim, trim: true));
        return CardUi.Card(body);
    }

    private static UIElement SegmentedBar(IReadOnlyList<AgentUsageSnapshot> snapshots, decimal total)
    {
        var bar = new Grid { Height = 6 };
        var track = new Border { Background = CardUi.Brush(CardUi.Track), CornerRadius = new CornerRadius(3) };
        bar.Children.Add(track);
        if (total <= 0)
        {
            return bar;
        }

        var segments = new Grid { CornerRadius = new CornerRadius(3) };
        var column = 0;
        foreach (var s in snapshots.Where(s => s.EstimatedCostUsdToday > 0))
        {
            segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)s.EstimatedCostUsdToday!.Value, GridUnitType.Star) });
            segments.Children.Add(CardUi.At(new Border { Background = CardUi.Brush(AccentFor(s.Kind)) }, column++));
        }

        bar.Children.Add(segments);
        return bar;
    }

    private static UIElement BuildNowCard(IReadOnlyList<AgentUsageSnapshot> snapshots)
    {
        var body = new StackPanel { Spacing = 8 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(CardUi.Icon("", 12, CardUi.Dim));
        head.Children.Add(CardUi.Text("Ahora", 13, Colors.White, bold: true));
        body.Children.Add(head);

        foreach (var s in snapshots)
        {
            var row = CardUi.Columns(GridLength.Auto, Star, GridLength.Auto);
            row.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = CardUi.Brush(AccentFor(s.Kind)), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(CardUi.At(CardUi.Text(ShortName(s.Kind), 13, Colors.White, bold: true, trim: true), 1));
            var when = s.IsLiveWorking
                ? CardUi.Text("trabajando", 12, Color.FromArgb(255, 0x30, 0xD1, 0x58))
                : CardUi.Text(s.LastActivityUtc is { } at ? "hace " + FormatAgoShort(DateTimeOffset.UtcNow - at) : "sin actividad", 12, CardUi.Dim);
            row.Children.Add(CardUi.At(when, 2));
            body.Children.Add(row);
        }

        return CardUi.Card(body);
    }

    private UIElement BuildTrendCard(IReadOnlyList<AgentUsageSnapshot> snapshots)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var days = _viewModel.History?.Last(today, 7) ?? [];
        var todayCost = snapshots.Sum(s => s.EstimatedCostUsdToday ?? 0);

        var body = new StackPanel { Spacing = 8 };
        var head = CardUi.Columns(GridLength.Auto, Star, GridLength.Auto);
        head.ColumnSpacing = 6;
        head.Children.Add(CardUi.Icon("", 12, CardUi.Dim));
        head.Children.Add(CardUi.At(CardUi.Text("Tendencia", 13, Colors.White, bold: true), 1));
        head.Children.Add(CardUi.At(CardUi.Text("Hoy · " + Money(todayCost), 12, CardUi.Dim), 2));
        body.Children.Add(head);

        var chart = new Grid { ColumnSpacing = 6, Height = 64 };
        var max = days.Count == 0 ? 0 : (double)days.Max(d => d.Cost);
        for (var i = 0; i < days.Count; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
            var isToday = i == days.Count - 1;
            var cost = (double)(isToday ? Math.Max(days[i].Cost, todayCost) : days[i].Cost);
            max = Math.Max(max, cost);
            var height = max <= 0 ? 4 : Math.Max(4, 56 * cost / max);
            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 3 };
            column.Children.Add(new Border
            {
                Height = height,
                CornerRadius = new CornerRadius(3),
                Background = CardUi.Brush(isToday ? AccentFor(AgentKind.ClaudeCode) : Color.FromArgb(0x55, 0xD9, 0x77, 0x57)),
                VerticalAlignment = VerticalAlignment.Bottom,
            });
            chart.Children.Add(CardUi.At(column, i));
        }

        body.Children.Add(chart);
        return CardUi.Card(body);
    }

    // ---------- formatting ----------

    private static string ShortName(AgentKind kind) => kind switch
    {
        AgentKind.ClaudeCode => "Claude",
        AgentKind.Codex => "Codex",
        AgentKind.Copilot => "Copilot",
        AgentKind.OpenCode => "OpenCode",
        _ => kind.ToString(),
    };

    private static Color Lighten(Color c) => Color.FromArgb(255, (byte)Math.Min(255, c.R + 40), (byte)Math.Min(255, c.G + 40), (byte)Math.Min(255, c.B + 40));

    private static string Money(decimal amount) => "$" + amount.ToString("0.00", CultureInfo.CurrentCulture);

    private static string FormatTokensShort(long n) => n switch
    {
        >= 1_000_000_000 => (n / 1_000_000_000.0).ToString("0.#", CultureInfo.CurrentCulture) + "B",
        >= 1_000_000 => (n / 1_000_000.0).ToString("0.#", CultureInfo.CurrentCulture) + "M",
        >= 1_000 => (n / 1_000.0).ToString("0.#", CultureInfo.CurrentCulture) + "K",
        _ => n.ToString(CultureInfo.CurrentCulture),
    };

    /// <summary>"47 min", "4 h 59 min", "2 d 4 h": time left until a plan window resets.</summary>
    private static string FormatResetIn(TimeSpan span) => span.TotalHours switch
    {
        < 1 => $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))} min",
        < 24 => $"{(int)span.TotalHours} h {span.Minutes} min",
        _ => $"{(int)span.TotalDays} d {span.Hours} h",
    };

    /// <summary>"3 min", "17 h", "4 d": how long ago something happened.</summary>
    private static string FormatAgoShort(TimeSpan span) => span.TotalMinutes switch
    {
        < 1 => "un momento",
        < 60 => $"{(int)span.TotalMinutes} min",
        < 1440 => $"{(int)span.TotalHours} h",
        _ => $"{(int)span.TotalDays} d",
    };
}
