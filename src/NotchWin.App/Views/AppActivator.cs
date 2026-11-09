using System.Runtime.InteropServices;

namespace NotchWin.App.Views;

/// <summary>
/// Abre la aplicación que envió una notificación a partir de su AUMID. Primero con la API de activación de Windows (IApplicationActivationManager, la misma que usa el Shell para
/// abrir apps de la Tienda): devuelve un código que dice si salió bien. Solo si falla, el método de antes (<c>explorer.exe shell:AppsFolder\AUMID</c>), que no informa de errores.
/// Cada intento deja una línea en %LOCALAPPDATA%\NotchWin\notificaciones.log (sin título ni texto de la notificación; solo el AUMID de la app y el resultado).
/// </summary>
internal static class AppActivator
{
    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] private class ApplicationActivationManager { }

    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, int options, out uint processId);
    }

    /// <summary>Un AUMID acaba en una línea de comandos: solo se admiten los caracteres que puede tener (sin comillas ni caracteres de control).</summary>
    public static bool IsSafe(string aumid) => aumid.Length is > 0 and <= 256 && !aumid.Any(c => c is '"' or '\'' or '<' or '>' or '|' or '%' || char.IsControl(c));

    public static bool Open(string aumid)
    {
        if (!IsSafe(aumid)) { Log(aumid, "rechazado: identificador no válido"); return false; }
        int hr = -1;
        try
        {
            var mgr = (IApplicationActivationManager)new ApplicationActivationManager();
            hr = mgr.ActivateApplication(aumid, null, 0 /* AO_NONE */, out _);
            if (hr >= 0) { Log(aumid, "abierta con la activación de Windows"); return true; }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException) { hr = ex is COMException ce ? ce.HResult : -2; }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"shell:AppsFolder\\" + aumid + "\"") { UseShellExecute = false })?.Dispose();
            Log(aumid, $"la activación falló (0x{hr:X8}); se pidió al Explorador, que no confirma si abrió");
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log(aumid, $"no se pudo abrir (activación 0x{hr:X8}; Explorador: {ex.GetType().Name})");
            return false;
        }
    }

    /// <summary>Deja constancia cuando una notificación no trajo identificador de app (no hay nada que abrir).</summary>
    public static void LogMissing(string reason) => Log("-", reason);

    private static void Log(string aumid, string what)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchWin"); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "notificaciones.log");
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {(aumid.Length > 80 ? aumid[..80] : aumid)} · {what}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // el diagnóstico nunca rompe el clic
        }
    }
}
