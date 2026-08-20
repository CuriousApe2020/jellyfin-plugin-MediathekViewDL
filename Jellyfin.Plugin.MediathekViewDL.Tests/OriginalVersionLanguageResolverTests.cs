using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediathekViewDL.Services.Media;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests;

public class OriginalVersionLanguageResolverTests
{
    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldReturnNull_WhenUrlIsNullOrEmpty()
    {
        var (resolver, _) = CreateResolver();

        Assert.Null(await resolver.TryGetOriginalVersionLanguageAsync(null, CancellationToken.None));
        Assert.Null(await resolver.TryGetOriginalVersionLanguageAsync(string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldDispatchToTheBroadcasterResolverThatCanHandleTheUrl()
    {
        var ardResolverMock = new Mock<IBroadcasterOriginalVersionLanguageResolver>();
        ardResolverMock.Setup(x => x.CanResolve(It.IsAny<string>())).Returns(false);

        var arteResolverMock = new Mock<IBroadcasterOriginalVersionLanguageResolver>();
        arteResolverMock.Setup(x => x.CanResolve("https://www.arte.tv/de/videos/109067-000-A/")).Returns(true);
        arteResolverMock
            .Setup(x => x.TryGetOriginalVersionLanguageAsync("https://www.arte.tv/de/videos/109067-000-A/", It.IsAny<CancellationToken>()))
            .ReturnsAsync("fra");

        var (resolver, _) = CreateResolver(new[] { ardResolverMock.Object, arteResolverMock.Object });

        var result = await resolver.TryGetOriginalVersionLanguageAsync("https://www.arte.tv/de/videos/109067-000-A/", CancellationToken.None);

        Assert.Equal("fra", result);
        ardResolverMock.Verify(x => x.TryGetOriginalVersionLanguageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryGetOriginalVersionLanguageAsync_ShouldReturnNull_WhenNoBroadcasterResolverCanHandleTheUrl()
    {
        var unrelatedResolverMock = new Mock<IBroadcasterOriginalVersionLanguageResolver>();
        unrelatedResolverMock.Setup(x => x.CanResolve(It.IsAny<string>())).Returns(false);

        var (resolver, _) = CreateResolver(new[] { unrelatedResolverMock.Object });

        var result = await resolver.TryGetOriginalVersionLanguageAsync("https://kika.de/video/12345", CancellationToken.None);

        Assert.Null(result);
    }

    private static (OriginalVersionLanguageResolver Resolver, Mock<ILogger<OriginalVersionLanguageResolver>> LoggerMock) CreateResolver(
        IEnumerable<IBroadcasterOriginalVersionLanguageResolver>? broadcasterResolvers = null)
    {
        var loggerMock = new Mock<ILogger<OriginalVersionLanguageResolver>>();
        var resolver = new OriginalVersionLanguageResolver(broadcasterResolvers ?? new List<IBroadcasterOriginalVersionLanguageResolver>(), loggerMock.Object);

        return (resolver, loggerMock);
    }
}
