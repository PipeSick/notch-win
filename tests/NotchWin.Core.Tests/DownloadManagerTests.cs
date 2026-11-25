using System.Net;
using NotchWin.Core.Downloads;
using Xunit;

namespace NotchWin.Core.Tests;

/// <summary>Servidor HTTP local mínimo (HttpListener) con Range, retardo por trozo y cabecera de nombre; sin red externa.</summary>
internal sealed class TestServer : IDisposable
{
    private readonly HttpListener _l = new(); private readonly CancellationTokenSource _cts = new();
    public string Base { get; }
    public byte[] Body { get; } = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();
    public bool SupportsRange = true; public int ChunkDelayMs; public bool SendLength = true; public string? Disposition; public int FailStatus;
    public int Requests;

    public TestServer()
    {
        int port; using (var s = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { s.Start(); port = ((IPEndPoint)s.LocalEndpoint).Port; }
        Base = $"http://localhost:{port}/"; _l.Prefixes.Add(Base); _l.Start(); _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext c; try { c = await _l.GetContextAsync(); } catch (Exception) { return; }
            _ = Task.Run(async () =>
            {
                Interlocked.Increment(ref Requests);
                try
                {
                    if (FailStatus != 0) { c.Response.StatusCode = FailStatus; c.Response.Close(); return; }
                    long start = 0; var range = c.Request.Headers["Range"];
                    if (SupportsRange && range is { } r && r.StartsWith("bytes=")) { start = long.Parse(r[6..].Split('-')[0]); c.Response.StatusCode = 206; c.Response.AddHeader("Content-Range", $"bytes {start}-{Body.Length - 1}/{Body.Length}"); }
                    if (Disposition is { } cd) c.Response.AddHeader("Content-Disposition", cd);
                    var len = Body.Length - start; if (SendLength) c.Response.ContentLength64 = len; else c.Response.SendChunked = true;
                    for (long off = start; off < Body.Length; off += 30_000)
                    {
                        int n = (int)Math.Min(30_000, Body.Length - off);
                        await c.Response.OutputStream.WriteAsync(Body.AsMemory((int)off, n)); await c.Response.OutputStream.FlushAsync();
                        if (ChunkDelayMs > 0) await Task.Delay(ChunkDelayMs);
                    }
                    c.Response.Close();
                }
                catch (Exception) { try { c.Response.Abort(); } catch (Exception) { } }
            });
        }
    }

    public void Dispose() { _cts.Cancel(); try { _l.Close(); } catch (Exception) { } }
}

public sealed class DownloadManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hatsu-dl-" + Guid.NewGuid().ToString("N")); private readonly TestServer _srv = new(); private readonly HttpClient _http = new();
    public DownloadManagerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { _srv.Dispose(); _http.Dispose(); try { Directory.Delete(_dir, true); } catch (Exception) { } }

    private static async Task Until(Func<bool> cond, int ms = 8000) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (!cond() && DateTime.UtcNow < end) await Task.Delay(15); Assert.True(cond(), "no se cumplió a tiempo"); }

    [Fact]
    public async Task A_download_completes_with_the_real_total_and_the_exact_bytes()
    {
        var m = new DownloadManager(_http, _dir); var d = m.Add(_srv.Base + "archivo.bin")!;
        await Until(() => d.State == ManagedState.Completed);
        Assert.Equal(_srv.Body.Length, d.TotalBytes); Assert.Equal(1.0, d.Fraction); Assert.Equal(_srv.Body, File.ReadAllBytes(Path.Combine(_dir, "archivo.bin"))); Assert.False(File.Exists(Path.Combine(_dir, "archivo.bin.hatsu-part")));
    }

    [Fact]
    public async Task Pause_keeps_the_partial_file_and_resume_continues_with_a_range_request_ending_with_identical_bytes()
    {
        _srv.ChunkDelayMs = 25; var m = new DownloadManager(_http, _dir); var d = m.Add(_srv.Base + "p.bin")!;
        await Until(() => d.ReceivedBytes > 60_000); m.Pause(d.Id); await Until(() => d.State == ManagedState.Paused);
        long at = d.ReceivedBytes; Assert.InRange(at, 1, _srv.Body.Length - 1); Assert.True(File.Exists(d.PartPath));
        _srv.ChunkDelayMs = 0; m.Resume(d.Id); await Until(() => d.State == ManagedState.Completed);
        Assert.Equal(_srv.Body, File.ReadAllBytes(Path.Combine(_dir, "p.bin"))); Assert.True(_srv.Requests >= 2);
    }

    [Fact]
    public async Task Cancel_deletes_the_partial_file_and_never_leaves_a_final_one()
    {
        _srv.ChunkDelayMs = 25; var m = new DownloadManager(_http, _dir); var d = m.Add(_srv.Base + "c.bin")!;
        await Until(() => d.ReceivedBytes > 30_000); m.Cancel(d.Id); await Until(() => d.State == ManagedState.Cancelled);
        Assert.False(File.Exists(Path.Combine(_dir, "c.bin"))); Assert.False(File.Exists(d.PartPath));
    }

    [Fact]
    public async Task A_server_without_range_support_restarts_from_zero_on_resume_and_still_ends_correct()
    {
        _srv.SupportsRange = false; _srv.ChunkDelayMs = 25; var m = new DownloadManager(_http, _dir); var d = m.Add(_srv.Base + "r.bin")!;
        await Until(() => d.ReceivedBytes > 60_000); m.Pause(d.Id); await Until(() => d.State == ManagedState.Paused);
        _srv.ChunkDelayMs = 0; m.Resume(d.Id); await Until(() => d.State == ManagedState.Completed);
        Assert.Equal(_srv.Body, File.ReadAllBytes(Path.Combine(_dir, "r.bin")));
    }

    [Fact]
    public async Task Without_a_content_length_there_is_no_total_or_percentage_so_the_bar_stays_indeterminate()
    {
        _srv.SendLength = false; _srv.ChunkDelayMs = 40; var m = new DownloadManager(_http, _dir); var d = m.Add(_srv.Base + "n.bin")!;
        await Until(() => d.ReceivedBytes > 10_000); Assert.Null(d.TotalBytes); Assert.Null(d.Fraction); Assert.Null(d.Remaining);
        await Until(() => d.State == ManagedState.Completed); Assert.Equal(_srv.Body.Length, d.ReceivedBytes);
    }

    [Fact]
    public async Task The_server_error_becomes_a_failed_state_with_a_plain_message_and_resume_retries()
    {
        _srv.FailStatus = 404; var m = new DownloadManager(_http, _dir); var d = m.Add(_srv.Base + "x.bin")!;
        await Until(() => d.State == ManagedState.Failed); Assert.Contains("404", d.Error);
        _srv.FailStatus = 0; m.Resume(d.Id); await Until(() => d.State == ManagedState.Completed);
    }

    [Fact]
    public async Task The_file_name_comes_from_the_server_header_and_is_always_a_safe_plain_name()
    {
        _srv.Disposition = "attachment; filename=\"..\\..\\evil/informe final.pdf\""; var m = new DownloadManager(_http, _dir); var d = m.Add(_srv.Base + "raw")!;
        await Until(() => d.State == ManagedState.Completed);
        Assert.Equal("informe final.pdf", d.FileName); Assert.Equal(_dir, Path.GetDirectoryName(d.TargetPath)); Assert.True(File.Exists(d.TargetPath));
    }

    [Fact]
    public async Task An_existing_file_is_never_overwritten_and_only_http_and_https_are_accepted()
    {
        File.WriteAllText(Path.Combine(_dir, "dup.bin"), "mio"); var m = new DownloadManager(_http, _dir); var d = m.Add(_srv.Base + "dup.bin")!;
        await Until(() => d.State == ManagedState.Completed);
        Assert.Equal("mio", File.ReadAllText(Path.Combine(_dir, "dup.bin"))); Assert.Equal("dup (1).bin", d.FileName);
        Assert.Null(m.Add("file:///C:/Windows/system32/config/SAM")); Assert.Null(m.Add("ftp://x/y")); Assert.Null(m.Add("no es una url")); Assert.Null(m.Add(null!));
    }

    [Theory]
    [InlineData("..\\..\\x.txt", "x.txt")] [InlineData("a/b/c.zip", "c.zip")] [InlineData("co:l?n*.txt", "co_l_n_.txt")] [InlineData("  ..  ", null)] [InlineData("", null)]
    public void File_names_are_sanitized(string input, string? expected) => Assert.Equal(expected, DownloadManager.SanitizeFileName(input));

    [Fact]
    public async Task At_most_three_downloads_run_at_once_and_the_rest_wait_in_the_queue()
    {
        _srv.ChunkDelayMs = 30; var m = new DownloadManager(_http, _dir); var all = Enumerable.Range(0, 5).Select(i => m.Add(_srv.Base + $"q{i}.bin")!).ToList();
        await Task.Delay(150); Assert.True(all.Count(d => d.State == ManagedState.Downloading) <= DownloadManager.MaxConcurrent); Assert.Contains(all, d => d.State == ManagedState.Queued);
        _srv.ChunkDelayMs = 0; await Until(() => all.All(d => d.State == ManagedState.Completed), 15000);
    }
}
