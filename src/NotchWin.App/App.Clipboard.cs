using NotchWin.Core.Clipboard;

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
