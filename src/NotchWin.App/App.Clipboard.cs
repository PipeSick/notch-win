using NotchWin.Core.Clipboard;
using NotchWin.Core.Settings;

namespace NotchWin.App;

/// <summary>
/// Clipboard options from WinMac (docs/28): limit, secrets, excluded apps and the OPTIONAL encrypted persistence. The file is encrypted for the current
/// Windows user with DPAPI (<c>DataProtectionProvider("LOCAL=user")</c>); turning persistence off deletes it. Content is never logged nor sent anywhere.
/// </summary>
public partial class App
{
    private System.Threading.Timer? _clipSave;
    private bool _clipRestored;

    private static string ClipboardFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchWin", "clipboard.dat");

    private sealed class Dpapi : IClipboardProtector
    {
        public byte[] Protect(byte[] d) => Run(async () =>
        {
            var p = new Windows.Security.Cryptography.DataProtection.DataProtectionProvider("LOCAL=user");
            var buf = await p.ProtectAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(d));
            return System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buf);
        });

        public byte[] Unprotect(byte[] d) => Run(async () =>
        {
            var p = new Windows.Security.Cryptography.DataProtection.DataProtectionProvider();
            var buf = await p.UnprotectAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(d));
            return System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buf);
        });

        private static byte[] Run(Func<Task<byte[]>> f) => Task.Run(f).GetAwaiter().GetResult();   // never on the UI thread's context
    }

    /// <summary>Spotlight asks for matching entries (only previews of what the history already admitted). Off when the clipboard tab is disabled.</summary>
    /// <summary>CI only (WINMAC_PILL_SELFTEST): counts and steps of the Spotlight queries, never content.</summary>
    private static void ClipDiag(string what)
    {
        if (Environment.GetEnvironmentVariable("WINMAC_PILL_SELFTEST") is not { Length: > 0 } st) return;
        try { File.AppendAllText(st, $"{DateTime.UtcNow:HH:mm:ss.fff} clip query {what}{Environment.NewLine}"); } catch (Exception) { }
    }

    private void OnClipQuery(long req, string? q)
    {
        if (_viewModel is null || _hosted is null) { ClipDiag($"sin modelo/sesión req={req}"); return; }
        bool on = _viewModel.Sections.Any(s => s.Id == NotchSections.Clipboard && s.Enabled);
        var found = on ? ClipboardQuery.Search(_viewModel.Clipboard, q) : [];
        _hosted.SendClipResults(req, on, found);
        ClipDiag($"respondida req={req} pestaña={on} total={_viewModel.Clipboard.Count} encontrados={found.Count} largo={q?.Length ?? 0}");
    }

    /// <summary>Spotlight chose an entry: the Island copies it (text, plain text or image) or removes it from the history.</summary>
    private async void OnClipAction(Guid id, string op)
    {
        if (_viewModel?.Clipboard is not { } h || h.All.FirstOrDefault(e => e.Id == id) is not { } entry) return;
        try
        {
            if (op == "delete") { h.Remove(id); return; }
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
            if (entry.Kind == ClipboardItemKind.Text) data.SetText(op == "plain" ? (entry.Text ?? "").Trim() : entry.Text ?? "");   // plain: only text, nothing else
            else if (entry.ImagePng is { } png)
            {
                var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await ms.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(png)); ms.Seek(0);
                data.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(ms));
            }
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        }
        catch (Exception) { /* locked clipboard: nothing happens */ }
    }

    private void ApplyClipboardOptions(ClipboardOptions o)
    {
        if (_viewModel is null) return;
        var h = _viewModel.Clipboard;
        h.Configure(o);
        if (!h.Options.Persist)
        {
            try { if (File.Exists(ClipboardFile)) File.Delete(ClipboardFile); } catch (IOException) { }   // persistence off: nothing stays on disk
            return;
        }

        if (!_clipRestored)
        {
            _clipRestored = true;
            Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(ClipboardFile)) return;
                    var entries = ClipboardStore.Deserialize(File.ReadAllBytes(ClipboardFile), new Dpapi());
                    _uiQueue?.TryEnqueue(() => h.Restore(entries));
                }
                catch (Exception) { /* unreadable (another user, corrupted): ignored and replaced on the next save */ }
            });
            h.Changed += ScheduleClipboardSave;
        }
    }

    private void ScheduleClipboardSave()
    {
        if (_viewModel?.Clipboard is not { Options.Persist: true } h) return;
        var snapshot = h.All.ToList();
        _clipSave?.Dispose();
        _clipSave = new System.Threading.Timer(_ =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ClipboardFile)!);
                var tmp = ClipboardFile + ".tmp";
                File.WriteAllBytes(tmp, ClipboardStore.Serialize(snapshot, new Dpapi()));
                File.Move(tmp, ClipboardFile, overwrite: true);
            }
            catch (Exception) { /* next change retries */ }
        }, null, 1500, Timeout.Infinite);
    }
}
