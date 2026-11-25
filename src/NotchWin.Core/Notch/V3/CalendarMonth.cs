using WinMac.Calendar;

namespace NotchWin.Core.Notch.V3;

public readonly record struct CalendarCell(DateOnly Date, bool InMonth, bool IsToday, int Events);

/// <summary>Cuadrícula del mes (semanas completas, lunes primero) y eventos de un día. Puro: sin Windows. Los eventos vienen de lo que el calendario configurado ya entrega; aquí no se inventa ninguno.</summary>
public static class CalendarMonth
{
    /// <summary>De 4 a 6 semanas de 7 días que cubren el mes. <paramref name="eventsOn"/> da cuántos eventos tiene un día (para el punto indicador).</summary>
    public static IReadOnlyList<IReadOnlyList<CalendarCell>> Grid(int year, int month, DateOnly today, Func<DateOnly, int>? eventsOn = null, DayOfWeek firstDay = DayOfWeek.Monday)
    {
        var first = new DateOnly(year, month, 1); var last = first.AddMonths(1).AddDays(-1);
        int lead = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
        var start = first.AddDays(-lead);
        int weeks = (int)Math.Ceiling((lead + last.Day) / 7.0);
        var rows = new List<IReadOnlyList<CalendarCell>>();
        for (int w = 0; w < weeks; w++)
        {
            var row = new List<CalendarCell>();
            for (int d = 0; d < 7; d++) { var date = start.AddDays(w * 7 + d); row.Add(new CalendarCell(date, date.Month == month && date.Year == year, date == today, eventsOn?.Invoke(date) ?? 0)); }
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Los de todo el día primero y luego por hora de inicio; un evento que cruza medianoche aparece en cada día que toca.</summary>
    public static IReadOnlyList<CalendarEvent> EventsOn(IEnumerable<CalendarEvent> events, DateOnly day)
    {
        var from = day.ToDateTime(TimeOnly.MinValue); var to = from.AddDays(1);
        return events.Where(e => e.Start.LocalDateTime < to && (e.End.LocalDateTime > from || (e.End == e.Start && e.Start.LocalDateTime >= from)))
            .OrderByDescending(e => e.AllDay).ThenBy(e => e.Start).ToList();
    }

    public static string MonthName(int month) => new[] { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" }[Math.Clamp(month, 1, 12) - 1];
}
