using System.Net;
using NotchWin.Core.Lyrics;

namespace NotchWin.Core.Tests;

public sealed class NeteaseLyricsClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
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

    private static HttpResponseMessage Responder(HttpRequestMessage request)
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
    public async Task PicksMatchingArtist_AndDropsCreditLines()
    {
        var client = new NeteaseLyricsClient(new HttpClient(new StubHandler(Responder)));

        var (lines, status) = await client.LookupAsync("Duro", "Skrillex", TimeSpan.FromSeconds(173));

        Assert.Equal(LyricsStatus.Found, status);
        Assert.Equal(["primera linea", "segunda"], lines!.Select(l => l.Text));
    }

    [Fact]
    public async Task RejectsTracksWithVeryDifferentDuration()
    {
        var client = new NeteaseLyricsClient(new HttpClient(new StubHandler(Responder)));

        var (lines, status) = await client.LookupAsync("Duro", "Skrillex", TimeSpan.FromSeconds(60));

        Assert.Null(lines);
        Assert.Equal(LyricsStatus.NotFound, status);
    }

    [Fact]
    public async Task NoResults_IsNotFound_AndIsCached()
    {
        var handler = new StubHandler(_ => Json("{\"result\":{\"songCount\":0}}"));
        var client = new NeteaseLyricsClient(new HttpClient(handler));

        var (_, first) = await client.LookupAsync("x", "y", null);
        var (_, second) = await client.LookupAsync("x", "y", null);

        Assert.Equal(LyricsStatus.NotFound, first);
        Assert.Equal(LyricsStatus.NotFound, second);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ServerError_IsUnavailable_AndRetriesNextTime()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var client = new NeteaseLyricsClient(new HttpClient(handler));

        var (lines, status) = await client.LookupAsync("x", "y", null);
        await client.LookupAsync("x", "y", null);

        Assert.Null(lines);
        Assert.Equal(LyricsStatus.Unavailable, status);
        Assert.Equal(2, handler.Calls);
    }
}
