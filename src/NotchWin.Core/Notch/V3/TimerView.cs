namespace NotchWin.Core.Notch.V3;

public enum TimerPhase { Ready, Running, Paused, Finished }

public enum TimerAction { Start, Pause, Resume, Reset }

/// <summary>
/// Qué ve y qué puede hacer el usuario según el estado REAL del temporizador. La cápsula y la pestaña usan estas mismas reglas, así que no pueden contradecirse.
/// «Cancelar» no es una acción aparte: es <see cref="TimerAction.Reset"/> (vuelve al tiempo elegido); no se muestran dos botones que hagan lo mismo.
/// </summary>
public static class TimerView
{
    public static TimerPhase Phase(bool isRunning, bool isFinished, TimeSpan remaining, TimeSpan total)
    {
        if (isFinished) return TimerPhase.Finished;
        if (isRunning) return TimerPhase.Running;
        return remaining > TimeSpan.Zero && remaining < total ? TimerPhase.Paused : TimerPhase.Ready;
    }

    public static IReadOnlyList<TimerAction> Actions(TimerPhase phase) => phase switch
    {
        TimerPhase.Ready => [TimerAction.Start],
        TimerPhase.Running => [TimerAction.Pause, TimerAction.Reset],
        TimerPhase.Paused => [TimerAction.Resume, TimerAction.Reset],
        _ => [TimerAction.Start, TimerAction.Reset],   // terminado: volver a empezar con el mismo tiempo, o volver a dejarlo listo
    };

    /// <summary>Cambiar el tiempo con el temporizador corriendo lo reiniciaría sin querer: solo se permite parado.</summary>
    public static bool PresetsEnabled(TimerPhase phase) => phase != TimerPhase.Running;

    public static string Label(TimerPhase phase) => phase switch { TimerPhase.Running => "En marcha", TimerPhase.Paused => "En pausa", TimerPhase.Finished => "Terminado", _ => "Listo" };

    public static string ActionLabel(TimerAction a, TimerPhase phase) => a switch
    {
        TimerAction.Start => phase == TimerPhase.Finished ? "Volver a empezar" : "Iniciar",
        TimerAction.Pause => "Pausar", TimerAction.Resume => "Reanudar", _ => "Reiniciar",
    };

    /// <summary>Presets rápidos del mockup aprobado (minutos).</summary>
    public static readonly IReadOnlyList<int> PresetMinutes = [5, 15, 25, 50, 90];
}
