using System;
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

public class ArdOriginalVersionLanguageResolverTests
{
    [Theory]
    [InlineData("https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw", true)]
    [InlineData("https://WWW.ARDMEDIATHEK.DE/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw", true)]
    [InlineData("https://zdf.de/video/12345678901234567890", false)]
    [InlineData("https://www.arte.tv/de/videos/109067-000-A/some-title/", false)]
    public void CanResolve_ShouldMatchOnlyArdMediathekUrls(string url, bool expected)
    {
        var (resolver, _) = CreateResolver();

        Assert.Equal(expected, resolver.CanResolve(url));
    }

    [Theory]
    // MediathekViewWeb's own short-form UrlWebsite (no slug/publisher segments).
    [InlineData("https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw")]
    // The "pretty" browser URL - the crid is still the last path segment.
    [InlineData("https://www.ardmediathek.de/video/some-show/some-episode/das-erste/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw")]
    // Trailing slash and query string must not become part of the id.
    [InlineData("https://www.ardmediathek.de/video/some-show/some-episode/das-erste/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw/?devicetype=pc")]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldUseLastPathSegmentAsItemId(string url)
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
                    Content = new StringContent("{\"ovLanguageCode\":\"eng\"}")
                });
            });

        var result = await resolver.TryGetOriginalVersionLanguageAsync(url, CancellationToken.None);

        Assert.Equal("eng", result);
        Assert.NotNull(capturedRequest);
        Assert.Contains("Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw", capturedRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldRequestTheMediaCollection()
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
                    Content = new StringContent("{}")
                });
            });

        await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw",
            CancellationToken.None);

        // Without this parameter the page-gateway answers with page metadata only - no media
        // collection, and therefore no language to find.
        Assert.NotNull(capturedRequest);
        Assert.Contains("mcV6=true", capturedRequest!.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldReadLanguageFromMediaCollection_WhenNoOvLanguageCodeExists()
    {
        // Shape confirmed against ARD's page-gateway:
        // widgets[].mediaCollection.embedded.streams[].media[].audios[] entries of
        // { "kind": ..., "languageCode": ... }.
        var (resolver, handlerMock) = CreateResolver();
        SetResponse(
            handlerMock,
            """
            {"widgets":[{"mediaCollection":{"embedded":{"streams":[
              {"kind":"main","media":[{"url":"https://example.invalid/de.mp4","audios":[{"kind":"standard","languageCode":"deu"}]}]},
              {"kind":"main","media":[{"url":"https://example.invalid/ov.mp4","audios":[{"kind":"standard","languageCode":"eng"}]}]}
            ]}}}]}
            """);

        var result = await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw",
            CancellationToken.None);

        Assert.Equal("eng", result);
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldPreferAnAudioTrackMarkedAsOriginalVersion()
    {
        var (resolver, handlerMock) = CreateResolver();
        SetResponse(
            handlerMock,
            """
            {"widgets":[{"mediaCollection":{"embedded":{"streams":[
              {"kind":"main","media":[{"audios":[{"kind":"standard","languageCode":"fra"}]}]},
              {"kind":"main","media":[{"audios":[{"kind":"original-version","languageCode":"nld"}]}]}
            ]}}}]}
            """);

        var result = await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw",
            CancellationToken.None);

        Assert.Equal("nld", result);
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldNormalizeTwoLetterAndLocaleCodes()
    {
        var (resolver, handlerMock) = CreateResolver();
        SetResponse(
            handlerMock,
            """{"widgets":[{"mediaCollection":{"embedded":{"streams":[{"media":[{"audios":[{"kind":"standard","languageCode":"en-GB"}]}]}]}}}]}""");

        var result = await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw",
            CancellationToken.None);

        Assert.Equal("eng", result);
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldFallBackToTheMediaCollection_WhenOvLanguageCodeIsUndetermined()
    {
        var (resolver, handlerMock) = CreateResolver();
        SetResponse(
            handlerMock,
            """{"ovLanguageCode":"und","widgets":[{"mediaCollection":{"embedded":{"streams":[{"media":[{"audios":[{"kind":"standard","languageCode":"eng"}]}]}]}}}]}""");

        var result = await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw",
            CancellationToken.None);

        Assert.Equal("eng", result);
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldReturnNull_WhenOnlyGermanAudioIsListed()
    {
        var (resolver, handlerMock) = CreateResolver();
        SetResponse(
            handlerMock,
            """
            {"widgets":[{"mediaCollection":{"embedded":{"streams":[
              {"kind":"main","media":[{"audios":[{"kind":"standard","languageCode":"deu"}]}]},
              {"kind":"audio-description","media":[{"audios":[{"kind":"audio-description","languageCode":"deu"}]}]}
            ]}}}]}
            """);

        var result = await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldIgnoreSubtitleLanguages()
    {
        var (resolver, handlerMock) = CreateResolver();
        SetResponse(
            handlerMock,
            """
            {"widgets":[{"mediaCollection":{"embedded":{
              "subtitles":[{"kind":"ebutt","languageCode":"eng","sources":[{"url":"https://example.invalid/sub.xml"}]}],
              "streams":[{"kind":"main","media":[{"audios":[{"kind":"standard","languageCode":"deu"}]}]}]
            }}}]}
            """);

        var result = await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw",
            CancellationToken.None);

        Assert.Null(result);
    }

    private static void SetResponse(Mock<HttpMessageHandler> handlerMock, string json)
    {
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldReturnNull_WhenItemIdLooksTooShort()
    {
        var (resolver, _) = CreateResolver();

        var result = await resolver.TryGetOriginalVersionLanguageAsync("https://www.ardmediathek.de/video/short", CancellationToken.None);

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
            "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldFindOvLanguageCode_WhenNestedDeeplyInResponse()
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
                Content = new StringContent("{\"widgets\":[{\"unrelated\":true},{\"mediaCollection\":{\"embedded\":{\"ovLanguageCode\":\"fra\"}}}]}")
            });

        var result = await resolver.TryGetOriginalVersionLanguageAsync(
            "https://www.ardmediathek.de/video/Y3JpZDovL2Rhc2Vyc3RlLmRlL2FiYzEyMw",
            CancellationToken.None);

        Assert.Equal("fra", result);
    }

    private static (ArdOriginalVersionLanguageResolver Resolver, Mock<HttpMessageHandler> HandlerMock) CreateResolver()
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
        var logger = new Mock<ILogger<ArdOriginalVersionLanguageResolver>>();
        var resolver = new ArdOriginalVersionLanguageResolver(httpClient, logger.Object);

        return (resolver, handlerMock);
    }
}
