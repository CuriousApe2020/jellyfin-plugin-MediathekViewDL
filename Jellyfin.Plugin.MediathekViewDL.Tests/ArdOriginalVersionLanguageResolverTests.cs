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
