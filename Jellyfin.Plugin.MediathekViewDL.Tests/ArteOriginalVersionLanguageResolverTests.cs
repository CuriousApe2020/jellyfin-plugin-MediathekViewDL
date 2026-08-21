using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class ArteOriginalVersionLanguageResolverTests
{
    // Trimmed to the "streams[].versions[]" array; captured from a real
    // GET https://api.arte.tv/api/player/v2/config/de/024862-000-A response
    // ("Rendezvous mit einer Leiche", an English-language film dubbed for German/French
    // broadcast). Confirms: the original-version track's "code" is prefixed "VO", its real
    // audio language ("en") lives in "audioLanguage" - NOT in "label", which here reads
    // "Originalfassung - UT französisch" and describes the *subtitle* language instead.
    private const string RealArteConfigResponseVersions = """
        {
          "data": {
            "attributes": {
              "streams": [
                {
                  "versions": [
                    { "code": "VA-STA", "label": "Deutsch", "shortLabel": "DE", "audioLanguage": "de", "subtitleLanguage": "und", "audioDescription": false },
                    { "code": "VA-STMA", "label": "Deutsch (Hörgeschädigte)", "shortLabel": "UT", "audioLanguage": "de", "subtitleLanguage": "de", "audioDescription": false },
                    { "code": "VF-STF", "label": "Französisch", "shortLabel": "FR", "audioLanguage": "fr", "subtitleLanguage": "und", "audioDescription": false },
                    { "code": "VO-STF", "label": "Originalfassung - UT französisch", "shortLabel": "OmU", "audioLanguage": "en", "subtitleLanguage": "fr", "audioDescription": false },
                    { "code": "VF-STMF", "label": "Französisch (Hörgeschädigte)", "shortLabel": "UT (frz.)", "audioLanguage": "fr", "subtitleLanguage": "fr", "audioDescription": false },
                    { "code": "VAAUD", "label": "Deutsch (Hörfilm)", "shortLabel": "AD (frz.)", "audioLanguage": "de", "subtitleLanguage": "und", "audioDescription": true },
                    { "code": "VFAUD", "label": "Französisch (Hörfilm)", "shortLabel": "AD (frz.)", "audioLanguage": "fr", "subtitleLanguage": "und", "audioDescription": true }
                  ]
                }
              ]
            }
          }
        }
        """;

    [Theory]
    [InlineData("https://www.arte.tv/de/videos/024862-000-A/some-title/", true)]
    [InlineData("https://www.ARTE.TV/de/videos/024862-000-A/some-title/", true)]
    [InlineData("https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw", false)]
    public void CanResolve_ShouldMatchOnlyArteUrls(string url, bool expected)
    {
        var (resolver, _) = CreateResolver();

        Assert.Equal(expected, resolver.CanResolve(url));
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldReturnNull_WhenUrlHasNoVideoId()
    {
        var (resolver, handlerMock) = CreateResolver();

        var result = await resolver.TryGetOriginalVersionLanguageAsync("https://www.arte.tv/de/some-page-without-an-id/", CancellationToken.None);

        Assert.Null(result);
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldRequestConfigApiForExtractedVideoId()
    {
        HttpRequestMessage? capturedRequest = null;
        var (resolver, handlerMock) = CreateResolver();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage req, CancellationToken _) =>
            {
                capturedRequest = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RealArteConfigResponseVersions)
                });
            });

        await resolver.TryGetOriginalVersionLanguageAsync("https://www.arte.tv/de/videos/024862-000-A/some-title/", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("https://api.arte.tv/api/player/v2/config/de/024862-000-A", capturedRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldReturnAudioLanguage_OfTheVoTaggedVersion()
    {
        var (resolver, handlerMock) = CreateResolver();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(RealArteConfigResponseVersions)
            });

        var result = await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.arte.tv/de/videos/024862-000-A/some-title/",
            CancellationToken.None);

        // The "VO-STF" entry's audioLanguage is "en" - its label ("Originalfassung - UT
        // französisch") never mentions English at all, only the French subtitle track, which is
        // exactly why this must come from "audioLanguage", not "label"/"shortLabel".
        Assert.Equal("eng", result);
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldReturnNull_WhenNoVoTaggedVersionExists()
    {
        var (resolver, handlerMock) = CreateResolver();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    { "data": { "attributes": { "streams": [ { "versions": [
                        { "code": "VA-STA", "label": "Deutsch", "audioLanguage": "de", "audioDescription": false },
                        { "code": "VAAUD", "label": "Deutsch (Hörfilm)", "audioLanguage": "de", "audioDescription": true }
                    ] } ] } } }
                    """)
            });

        var result = await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.arte.tv/de/videos/024862-000-A/some-title/",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldReturnNull_WhenApiCallFails()
    {
        var (resolver, handlerMock) = CreateResolver();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.arte.tv/de/videos/024862-000-A/some-title/",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldReturnNull_WhenResponseIsMalformed()
    {
        var (resolver, handlerMock) = CreateResolver();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not valid json")
            });

        var result = await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.arte.tv/de/videos/024862-000-A/some-title/",
            CancellationToken.None);

        Assert.Null(result);
    }

    private static (ArteOriginalVersionLanguageResolver Resolver, Mock<HttpMessageHandler> HandlerMock) CreateResolver()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var httpClient = new HttpClient(handlerMock.Object);
        var logger = new Mock<ILogger<ArteOriginalVersionLanguageResolver>>();
        var resolver = new ArteOriginalVersionLanguageResolver(httpClient, logger.Object);

        return (resolver, handlerMock);
    }
}
