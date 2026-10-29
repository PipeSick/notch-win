using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace NotchWin.App;

/// <summary>
/// Hand-written entry point required for an *unpackaged* WinUI 3 desktop app (no MSIX identity,
/// so none of the AppX activation plumbing applies) — this is the standard boilerplate from the
/// Windows App SDK's own "Unpackaged" project template, not something specific to NotchWin.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
