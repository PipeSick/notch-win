using System.Net;
using NotchWin.Core.Lyrics;

namespace NotchWin.Core.Tests;

public sealed class LyricsFallbackTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private const string SearchBody = """
        {"result":{"songs":[
          {"id":1,"name":"Duro (Live)","duration":300000,"artists":[{"name":"Someone Else"}]},
          {"id":2,"name":"Duro","duration":185600,"artists":[{"name":"Skrillex"},{"name":"Young Miko"}]}
        ]}}
        """;

    private static HttpResponseMessage NeteaseResponder(HttpRequestMessage request)
    {
        var url = request.RequestUri!.ToString();
        if (url.Contains("/api/search/get"))
        {
            return Json(SearchBody);
        }

        Assert.Contains("id=2", url); // the artist-matching track, not the live version
        return Json("{\"lrc\":{\"lyric\":\"[00:00.000] 作词 : Someone\\n[00:00.000] Producer : Skrillex\\n[00:12.50]primera linea\\n[00:20.00]segunda\"}}");
    }

    [Fact]
    public async Task Netease_PicksMatchingArtist_AndDropsCreditLines()
    {
        var handler = new StubHandler(NeteaseResponder);
        var client = new NeteaseLyricsClient(new HttpClient(handler));

        var (lines, status) = await client.LookupAsync("Duro", "Skrillex", TimeSpan.FromSeconds(173));

        Assert.Equal(LyricsStatus.Found, status);
        Assert.Equal(["primera linea", "segunda"], lines!.Select(l => l.Text));
    }

    [Fact]
    public async Task Netease_RejectsTracksWithVeryDifferentDuration()
    {
        var handler = new StubHandler(NeteaseResponder);
        var client = new NeteaseLyricsClient(new HttpClient(handler));

        var (lines, status) = await client.LookupAsync("Duro", "Skrillex", TimeSpan.FromSeconds(60));

        Assert.Null(lines);
        Assert.Equal(LyricsStatus.NotFound, status);
    }

    [Fact]
    public async Task Provider_FallsBackToNetease_WhenLrclibIsDown()
    {
        var lrclib = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var netease = new StubHandler(NeteaseResponder);
        var provider = new LyricsProvider(new LrclibClient(new HttpClient(lrclib)), new NeteaseLyricsClient(new HttpClient(netease)));

        var (lines, status) = await provider.LookupAsync("Duro", "Skrillex", TimeSpan.FromSeconds(173));

        Assert.Equal(LyricsStatus.Found, status);
        Assert.Equal(2, lines!.Count);
    }

    [Fact]
    public async Task Provider_ReportsUnavailable_WhenOneSourceIsDownAndTheOtherHasNothing()
    {
        var lrclib = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var netease = new StubHandler(_ => Json("{\"result\":{\"songCount\":0}}"));
        var provider = new LyricsProvider(new LrclibClient(new HttpClient(lrclib)), new NeteaseLyricsClient(new HttpClient(netease)));

        var (lines, status) = await provider.LookupAsync("x", "y", null);

        Assert.Null(lines);
        Assert.Equal(LyricsStatus.Unavailable, status);
    }

    [Fact]
    public async Task Provider_ReportsNotFound_WhenBothSourcesAnswerWithNothing()
    {
        var lrclib = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var netease = new StubHandler(_ => Json("{\"result\":{\"songCount\":0}}"));
        var provider = new LyricsProvider(new LrclibClient(new HttpClient(lrclib)), new NeteaseLyricsClient(new HttpClient(netease)));

        var (_, status) = await provider.LookupAsync("x", "y", null);

        Assert.Equal(LyricsStatus.NotFound, status);
    }

    [Fact]
    public async Task Provider_SkipsNetease_WhenLrclibHasTheLyrics()
    {
        var lrclib = new StubHandler(_ => Json("{\"syncedLyrics\":\"[00:01.00]hola\"}"));
        var netease = new StubHandler(_ => throw new InvalidOperationException("should not be called"));
        var provider = new LyricsProvider(new LrclibClient(new HttpClient(lrclib)), new NeteaseLyricsClient(new HttpClient(netease)));

        var (lines, status) = await provider.LookupAsync("a", "b", null);

        Assert.Equal(LyricsStatus.Found, status);
        Assert.Equal("hola", lines![0].Text);
        Assert.Empty(netease.Urls);
    }
}
