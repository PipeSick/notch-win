using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel;
using Windows.Foundation;

namespace NotchWin.App.Views;

/// <summary>
/// Icono real de la app que envió una notificación, a partir de su AUMID (el identificador que Windows entrega con el aviso). Solo funciona para apps empaquetadas que Windows deje consultar;
/// para el resto (o ante cualquier error) devuelve null y la vista dibuja la inicial del nombre. Se guarda en una caché pequeña para no volver a pedirlo en cada repintado.
/// </summary>
internal static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Peek(string? aumid) => aumid is not null && Cache.TryGetValue(aumid, out var s) ? s : null;

    /// <summary>Carga (una vez por app) el icono; <paramref name="done"/> corre en el hilo de interfaz si hay icono nuevo para mostrar.</summary>
    public static async Task EnsureAsync(string? aumid, Action done)
    {
        if (string.IsNullOrWhiteSpace(aumid) || Cache.ContainsKey(aumid) || !Pending.Add(aumid)) return;
        ImageSource? image = null;
        try
        {
            var info = AppInfo.GetFromAppUserModelId(aumid);
            var logo = info?.DisplayInfo.GetLogo(new Size(48, 48));
            if (logo is not null)
            {
                using var stream = await logo.OpenReadAsync();
                var bmp = new BitmapImage { DecodePixelWidth = 48 };
                await bmp.SetSourceAsync(stream);
                image = bmp;
            }
        }
        catch (Exception)
        {
            image = null;   // app sin empaquetar, no visible para este proceso o sin logo: se usa la inicial
        }

        Cache[aumid] = image; Pending.Remove(aumid);
        if (image is not null) done();
    }
}
