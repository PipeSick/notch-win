using System.Text;
using NotchWin.Core.Clipboard;
using Xunit;

namespace NotchWin.Core.Tests;

// Portapapeles avanzado (docs/28): datos de PRUEBA inventados para el test (no son credenciales reales).
public class ClipboardPrivacyTests
{
    [Theory]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIE...")]
    [InlineData("AKIAABCDEFGHIJKLMNOP")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U")]
    [InlineData("password = hunter2hunter2")]
    [InlineData("Xk9#vQ2!mLp7$wR4")]
    public void SecretsAreRecognised(string text) => Assert.True(SecretDetector.LooksSecret(text));

    [Theory]
    [InlineData("Hola, nos vemos mañana a las 10")]
    [InlineData("https://github.com/PipeSick/win11-macos/pull/12?tab=files")]
    [InlineData("C:\\Users\\ana\\Documents\\Informe-2026_final.docx")]
    [InlineData("2026-11-02")]
    [InlineData("WinMacScreenshot")]
    public void OrdinaryTextIsKept(string text) => Assert.False(SecretDetector.LooksSecret(text));

    [Fact]
    public void HistorySkipsSecretsAndHonoursTheLimit()
    {
        var h = new ClipboardHistory();
        h.Configure(new ClipboardOptions { Limit = 5 });
        h.AddText("ghp_abcdefghijklmnopqrstuvwxyz0123456789", DateTimeOffset.Now);
        Assert.Equal(0, h.Count); Assert.Equal(1, h.Skipped);
        for (int i = 0; i < 9; i++) h.AddText("nota " + i, DateTimeOffset.Now.AddSeconds(i));
        Assert.Equal(5, h.Count);
        h.Configure(new ClipboardOptions { Limit = 1 });   // clamped to the minimum
        Assert.Equal(5, h.Count);
    }

    [Fact]
    public void ExcludedAppsMatchByProcessName()
    {
        var o = new ClipboardOptions { ExcludedApps = ["KeePassXC.exe", " miapp "] }.Sanitize();
        Assert.True(o.IsExcluded("keepassxc")); Assert.True(o.IsExcluded("MiApp.exe")); Assert.False(o.IsExcluded("notepad"));
    }

    private sealed class XorProtector : IClipboardProtector   // stand-in for DPAPI in tests
    {
        public byte[] Protect(byte[] d) => d.Select(b => (byte)(b ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] d) => Protect(d);
    }

    [Fact]
    public void PersistedBlobRoundTripsAndIsNotPlainText()
    {
        var p = new XorProtector();
        var entries = new[] { new ClipboardEntry { Kind = ClipboardItemKind.Text, Text = "texto de prueba", At = DateTimeOffset.Now, Pinned = true },
                              new ClipboardEntry { Kind = ClipboardItemKind.Image, ImagePng = [1, 2, 3], At = DateTimeOffset.Now } };
        var blob = ClipboardStore.Serialize(entries, p);
        Assert.DoesNotContain("texto de prueba", Encoding.UTF8.GetString(blob));
        var back = ClipboardStore.Deserialize(blob, p);
        Assert.Equal(2, back.Count); Assert.True(back[0].Pinned); Assert.Equal([1, 2, 3], back[1].ImagePng);
        Assert.Empty(ClipboardStore.Deserialize([1, 2, 3], p));   // corrupt: ignored
    }
}

public class ClipboardQueryTests
{
    [Fact]
    public void SpotlightSeesOnlyAdmittedEntriesAsShortPreviews()
    {
        var h = new ClipboardHistory();
        h.AddText("ghp_abcdefghijklmnopqrstuvwxyz0123456789", DateTimeOffset.Now);           // secret: never admitted
        h.AddText("Lista de la compra:\npan\nleche", DateTimeOffset.Now.AddSeconds(1));
        h.AddText(new string('x', 400) + " fin", DateTimeOffset.Now.AddSeconds(2));
        h.AddImage([1, 2, 3], DateTimeOffset.Now.AddSeconds(3));
        var all = ClipboardQuery.Search(h, "portapapeles");
        Assert.Equal(3, all.Count); Assert.DoesNotContain(all, i => i.Preview.Contains("ghp_"));
        Assert.Equal("Imagen", all[0].Preview);
        Assert.True(all.All(i => i.Preview.Length <= 160));
        var found = ClipboardQuery.Search(h, "cb leche"); Assert.Equal("Lista de la compra: pan leche", Assert.Single(found).Preview);
    }
}
