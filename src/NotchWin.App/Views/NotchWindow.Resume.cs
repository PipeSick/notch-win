namespace NotchWin.App.Views;

public sealed partial class NotchWindow
{
    /// <summary>Tras suspender: se reconecta el cristal y se vuelve a pintar la superficie (lo posiciona App.OnResumed).</summary>
    public void RefreshAfterResume()
    {
        if (_closed) return;
        try { _glass.Reconnect(); UpdateSurface(); }
        catch (Exception) { /* la próxima vuelta del guardián lo intenta de nuevo */ }
    }
}
