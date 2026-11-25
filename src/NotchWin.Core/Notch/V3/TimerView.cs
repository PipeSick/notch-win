namespace NotchWin.Core.Notch.V3;

public enum TimerPhase { Ready, Running, Paused, Finished }

public enum TimerAction { Start, Pause, Resume, Restart, Cancel }

/// <summary>
/// Qué ve y qué puede hacer el usuario según el estado REAL del temporizador. La cápsula, Inicio y la pestaña usan estas mismas reglas, así que no pueden contradecirse.
/// Semántica distinta: <b>Reiniciar</b> = vuelve al tiempo completo y sigue corriendo; <b>Cancelar</b> = descarta el temporizador (queda «listo», y deja de verse en la cápsula e Inicio).
/// </summary>
public static class TimerView
{
    public static TimerPhase Phase(bool isRunning, bool isFinished, bool isArmed)
    {
        if (isFinished) return TimerPhase.Finished;
        if (isRunning) return TimerPhase.Running;
        return isArmed ? TimerPhase.Paused : TimerPhase.Ready;
    }

    public static TimerPhase Phase(CountdownTimerState t) => Phase(t.IsRunning, t.IsFinished, t.IsArmed);

    public static IReadOnlyList<TimerAction> Actions(TimerPhase phase) => phase switch
    {
        TimerPhase.Ready => [TimerAction.Start],
        TimerPhase.Running => [TimerAction.Pause, TimerAction.Restart, TimerAction.Cancel],
        TimerPhase.Paused => [TimerAction.Resume, TimerAction.Restart, TimerAction.Cancel],
        _ => [TimerAction.Restart, TimerAction.Cancel],   // terminado: volver a empezar o descartar
    };

    /// <summary>Cambiar el tiempo con el temporizador corriendo lo reiniciaría sin querer: solo se permite parado.</summary>
    public static bool PresetsEnabled(TimerPhase phase) => phase != TimerPhase.Running;

    /// <summary>La cápsula y Inicio solo muestran un temporizador «en juego» (corriendo, en pausa o recién terminado).</summary>
    public static bool ShowsInCapsule(TimerPhase phase) => phase != TimerPhase.Ready;

    public static string Label(TimerPhase phase) => phase switch { TimerPhase.Running => "En marcha", TimerPhase.Paused => "En pausa", TimerPhase.Finished => "Terminado", _ => "Listo" };

    public static string ActionLabel(TimerAction a, TimerPhase phase) => a switch
    {
        TimerAction.Start => "Iniciar", TimerAction.Pause => "Pausar", TimerAction.Resume => "Reanudar",
        TimerAction.Restart => phase == TimerPhase.Finished ? "Volver a empezar" : "Reiniciar", _ => phase == TimerPhase.Finished ? "Descartar" : "Cancelar",
    };

    /// <summary>Presets rápidos del mockup aprobado (minutos).</summary>
    public static readonly IReadOnlyList<int> PresetMinutes = [5, 15, 25, 50, 90];
}

/// <summary>Lo mínimo que <see cref="TimerView"/> necesita saber de un temporizador (lo cumple <see cref="Timers.CountdownTimer"/> mediante el adaptador de <see cref="TimerStates"/>).</summary>
public readonly record struct CountdownTimerState(bool IsRunning, bool IsFinished, bool IsArmed);

public static class TimerStates
{
    public static CountdownTimerState Of(Timers.CountdownTimer t) => new(t.IsRunning, t.IsFinished, t.IsArmed);
}
