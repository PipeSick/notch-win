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

/// <summary>WM-008 («Fighting Myself — Linkin Park» without lyrics in the beta): refusals, decorated titles and the second source.</summary>
public sealed class LyricsWm008Tests
{
    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private const string Lrclib = """
        [{"trackName":"﻿Fighting Myself","artistName":"﻿Linkin Park","duration":200,"syncedLyrics":"[00:10.00]primera\n[00:15.00]segunda"},
         {"trackName":"Fighting Myself","artistName":"Linkin Park","duration":200,"syncedLyrics":null}]
        """;

    [Fact]
    public async Task A_netease_refusal_is_unavailable_and_not_cached_as_not_found()
    {
        var stub = new Stub(_ => Json("""{"code":-460,"message":"Cheating"}"""));
        var client = new NeteaseLyricsClient(new HttpClient(stub));
        var (lines, status) = await client.LookupAsync("Fighting Myself", "Linkin Park", TimeSpan.FromSeconds(201));
        Assert.Null(lines); Assert.Equal(LyricsStatus.Unavailable, status); Assert.Contains("-460", client.LastDiagnosis);
        await client.LookupAsync("Fighting Myself", "Linkin Park", TimeSpan.FromSeconds(201));
        Assert.Equal(2, stub.Urls.Count);   // asked again: a refusal is not «this song has no lyrics»
    }

    [Fact]
    public async Task Lrclib_is_asked_only_when_netease_has_nothing_and_bom_in_names_is_ignored()
    {
        var stub = new Stub(r => r.RequestUri!.Host == "lrclib.net" ? Json(Lrclib) : Json("""{"result":{"songs":[]},"code":200}"""));
        var http = new HttpClient(stub);
        var client = new NeteaseLyricsClient(http, new LrclibLyricsClient(http));
        var (lines, status) = await client.LookupAsync("Fighting Myself", "Linkin Park", TimeSpan.FromSeconds(201));
        Assert.Equal(LyricsStatus.Found, status); Assert.Equal(["primera", "segunda"], lines!.Select(l => l.Text));
        Assert.Contains("LRCLIB: letra sincronizada", client.LastDiagnosis);
        Assert.DoesNotContain("Fighting", client.LastDiagnosis);   // the diagnosis never carries the title or the artist
    }

    [Fact]
    public async Task Netease_found_means_lrclib_is_never_contacted()
    {
        var stub = new Stub(r => r.RequestUri!.ToString().Contains("/api/search/get")
            ? Json("""{"result":{"songs":[{"id":7,"name":"Fighting Myself","duration":201000,"artists":[{"name":"Linkin Park"}]}]},"code":200}""")
            : Json("""{"lrc":{"lyric":"[00:12.00]una"}}"""));
        var http = new HttpClient(stub);
        var client = new NeteaseLyricsClient(http, new LrclibLyricsClient(http));
        var (_, status) = await client.LookupAsync("Fighting Myself", "Linkin Park", TimeSpan.FromSeconds(201));
        Assert.Equal(LyricsStatus.Found, status); Assert.DoesNotContain(stub.Urls, u => u.Contains("lrclib"));
    }

    [Theory]
    [InlineData("Numb - Remastered 2023", "Numb")]
    [InlineData("In the End (feat. Someone)", "In the End")]
    [InlineData("Faint [Live]", "Faint")]
    [InlineData("Fighting Myself", "Fighting Myself")]
    [InlineData("Re-Make - Original Mix", "Re-Make")]
    [InlineData("Up - Down", "Up - Down")]   // a dash that is part of the name stays
    public void Bare_title_drops_only_player_decorations(string title, string bare) => Assert.Equal(bare, NeteaseLyricsClient.BareTitle(title));

    [Fact]
    public async Task A_decorated_title_is_retried_bare_on_netease()
    {
        var stub = new Stub(r => r.RequestUri!.ToString().Contains("/api/search/get")
            ? Json("""{"result":{"songs":[{"id":9,"name":"Numb","duration":185000,"artists":[{"name":"Linkin Park"}]}]},"code":200}""")
            : Json("""{"lrc":{"lyric":"[00:05.00]linea"}}"""));
        var client = new NeteaseLyricsClient(new HttpClient(stub));
        var (lines, status) = await client.LookupAsync("Numb - Remastered 2023 Version", "Linkin Park", TimeSpan.FromSeconds(186));
        Assert.Equal(LyricsStatus.Found, status); Assert.Single(lines!);
    }
}
