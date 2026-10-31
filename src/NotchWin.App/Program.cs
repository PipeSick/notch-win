using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NotchWin.Core.Hosting;

namespace NotchWin.App;

/// <summary>
/// Hand-written entry point required for an *unpackaged* WinUI 3 desktop app (no MSIX identity,
/// so none of the AppX activation plumbing applies) — this is the standard boilerplate from the
/// Windows App SDK's own "Unpackaged" project template, not something specific to NotchWin.
/// </summary>
public static class Program
{
    /// <summary>The link to WinMac when this process was started by it (<c>--hosted --pipe …</c>); null when NotchWin runs on its own, exactly as before.</summary>
    internal static HostedSession? Hosted { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        var hosted = HostedSession.FromArgs(args, typeof(Program).Assembly.GetName().Version?.ToString() ?? "0");

        // One NotchWin per user session: a second one would create a second notch on every monitor.
        using var single = new Mutex(true, @"Local\NotchWin.SingleInstance", out var first);
        if (!first)
        {
            if (hosted is not null)
            {
                // Started by WinMac while another NotchWin is already running (e.g. a standalone one): say the exit is deliberate so WinMac does not treat it as a crash and restart it.
                hosted.Start();
                hosted.ByeAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                hosted.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return;
        }

        Hosted = hosted;
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        GC.KeepAlive(single);
    }
}
