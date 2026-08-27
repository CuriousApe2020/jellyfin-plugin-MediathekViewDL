using Jellyfin.Plugin.MediathekViewDL.Channels;
using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration;
using Jellyfin.Plugin.MediathekViewDL.Services.Subscriptions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MediathekViewDL.Tests
{
    public class MediathekChannelTests
    {
        private readonly Mock<ILogger<MediathekChannel>> _loggerMock;
        private readonly Mock<IConfigurationProvider> _configProviderMock;
        private readonly Mock<ISubscriptionProcessor> _subscriptionProcessorMock;
        private readonly MediathekChannel _channel;

        public MediathekChannelTests()
        {
            _loggerMock = new Mock<ILogger<MediathekChannel>>();
            _configProviderMock = new Mock<IConfigurationProvider>();
            _subscriptionProcessorMock = new Mock<ISubscriptionProcessor>();

            _channel = new MediathekChannel(
                _loggerMock.Object,
                _configProviderMock.Object,
                _subscriptionProcessorMock.Object);
        }

        [Fact]
        public void IsEnabledFor_ReturnsFalse_WhenConfigurationIsUnavailable()
        {
            // Arrange
            _configProviderMock.Setup(x => x.ConfigurationOrNull).Returns((PluginConfiguration?)null);

            // Act / Assert
            Assert.False(_channel.IsEnabledFor("user-1"));
        }

        [Fact]
        public void IsEnabledFor_ReturnsFalse_WhenNoSubscriptionIsMarkedVirtual()
        {
            // Arrange: the channel is registered unconditionally at plugin startup, so it must not
            // show up in Jellyfin's channel list for installations that never use virtual
            // subscriptions - only a normal, downloaded subscription exists here.
            var config = new PluginConfiguration();
            config.Subscriptions.Add(new Subscription { Name = "Normal", IsEnabled = true, IsVirtual = false });
            _configProviderMock.Setup(x => x.ConfigurationOrNull).Returns(config);

            // Act / Assert
            Assert.False(_channel.IsEnabledFor("user-1"));
        }

        [Fact]
        public void IsEnabledFor_ReturnsFalse_WhenTheOnlyVirtualSubscriptionIsDisabled()
        {
            // Arrange
            var config = new PluginConfiguration();
            config.Subscriptions.Add(new Subscription { Name = "Virtual but disabled", IsEnabled = false, IsVirtual = true });
            _configProviderMock.Setup(x => x.ConfigurationOrNull).Returns(config);

            // Act / Assert
            Assert.False(_channel.IsEnabledFor("user-1"));
        }

        [Fact]
        public void IsEnabledFor_ReturnsTrue_WhenAnEnabledVirtualSubscriptionExists()
        {
            // Arrange
            var config = new PluginConfiguration();
            config.Subscriptions.Add(new Subscription { Name = "Normal", IsEnabled = true, IsVirtual = false });
            config.Subscriptions.Add(new Subscription { Name = "Virtual", IsEnabled = true, IsVirtual = true });
            _configProviderMock.Setup(x => x.ConfigurationOrNull).Returns(config);

            // Act / Assert
            Assert.True(_channel.IsEnabledFor("user-1"));
        }
    }
}
