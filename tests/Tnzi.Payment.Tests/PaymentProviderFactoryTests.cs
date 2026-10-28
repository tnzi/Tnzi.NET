using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 列一次已启用渠道不该把每个没启用的渠道都记成一条 Warning。
/// </summary>
/// <remarks>
/// <c>GetEnabledProviders</c> 此前逐个调 <c>GetProvider</c>，而后者每判一次不可用就记一条 Warning：
/// 结账页每渲染一次可选渠道，日志里就多出一批「渠道未启用」，真正的配置问题被淹没在里面。
/// </remarks>
public class PaymentProviderFactoryTests
{
    [Fact]
    public void ListingEnabledChannels_ReturnsOnlyEnabledOnes_WithoutWarnings()
    {
        var options = new PaymentOptions
        {
            AllowTestProvider = false,
            Channels = new Dictionary<string, ChannelOptions>
            {
                [PaymentConstants.OfflineChannelCode] = new() { Enabled = true }
            }
        };
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.Setup(x => x.CurrentValue).Returns(options);
        var logger = new Mock<ILogger<PaymentProviderFactory>>();
        IPaymentProvider[] providers =
        [
            new OfflineProvider(NullLogger<OfflineProvider>.Instance),
            new NullProvider(NullLogger<NullProvider>.Instance)
        ];

        var factory = new PaymentProviderFactory(providers, monitor.Object, logger.Object);

        factory.GetEnabledProviders().Select(p => p.ChannelCode).ShouldBe([PaymentConstants.OfflineChannelCode]);
        logger.Verify(
            l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
