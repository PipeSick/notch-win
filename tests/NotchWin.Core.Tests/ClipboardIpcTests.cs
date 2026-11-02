using System.Text.Json;
using NotchWin.Core.Clipboard;
using NotchWin.Core.Hosting;
using WinMac.Ipc;
using Xunit;

namespace NotchWin.Core.Tests;

// Integración REAL del canal (named pipe de WinMac en Linux): la Barra pregunta por el portapapeles y la Isla responde (docs/28).
public class ClipboardIpcTests
{
    private static async Task<bool> Until(Func<bool> c, int ms = 6000) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { if (c()) return true; await Task.Delay(20); } return c(); }

    [Fact]
    public async Task SpotlightQueryReachesTheIslandAndItsAnswerReachesTheBar()
    {
        string pipe = "nw-clip-" + Guid.NewGuid().ToString("N"), token = "t" + Guid.NewGuid().ToString("N");
        await using var server = new IpcServer(new ServerOptions(pipe, token)); server.Start();
        Environment.SetEnvironmentVariable(ModuleHostLink.TokenEnvVar, token);
        var session = HostedSession.FromArgs(["--hosted", "--pipe", pipe])!;
        await using var _ = session;
        var h = new ClipboardHistory(); h.AddText("Lista de la compra", DateTimeOffset.Now);
        long? asked = null; string? askedQ = null;
        session.ClipQuery += (req, q) => { asked = req; askedQ = q; session.SendClipResults(req, true, ClipboardQuery.Search(h, q)); };
        session.Start();

        await using var bar = new IpcClient(new ClientOptions(pipe, token, "bar") { MaxBackoff = TimeSpan.FromMilliseconds(200) });
        JsonElement? answer = null;
        bar.Subscribe(HostedSession.ClipResultsTopic, d => answer = d?.Clone());
        bar.Start();
        Assert.True(await Until(() => server.IsConnected("notch") && server.IsConnected("bar")));
        await Task.Delay(300);   // subscriptions registered
        bar.Event(HostedSession.ClipQueryTopic, new { req = 7L, q = "portapapeles" });
        Assert.True(await Until(() => answer is not null), "la respuesta no llegó a la Barra");
        Assert.Equal(7, asked); Assert.Equal("portapapeles", askedQ);
        var items = answer!.Value.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("Lista de la compra", items[0].GetProperty("preview").GetString());
        Assert.Equal(32, items[0].GetProperty("id").GetString()!.Length);
    }
}
