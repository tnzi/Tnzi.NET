using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Domain.Repositories;
using Tnzi.Modules;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 对接第三方的渠道现在各自住在可选子模块里（Stripe 在 <c>Tnzi.Payment.Stripe</c>，
/// PayPal 在 <c>Tnzi.Payment.PayPal</c>）。这批用例锁的是<b>不加载它们时</b>本模块的表现：
/// 少一个渠道，不是行为出错。
/// </summary>
/// <remarks>
/// <para>
/// 两条各自防一种失效：
/// </para>
/// <list type="number">
/// <item>配置里把渠道 <c>Enabled=true</c> 也<b>换不来</b>一个实现 —— 配置开关管的是「发不发」，
/// 不是「有没有」。这条防的是把 <c>Payment:Channels</c> 误当成渠道注册表。</item>
/// <item>渠道的 webhook 打进来时必须<b>拒绝</b>而不是回 200。回 200 等于告诉渠道
/// 「这笔事件我处理过了」，而本地状态机一步都没走 —— 渠道不再重投，那笔支付永远停在处理中。
/// 这是这条路径上唯一能变成「谎报成功」的地方，也正是拆包最需要钉住的地方。</item>
/// </list>
/// </remarks>
public class ChannelPackageAbsenceTests
{
    /// <summary>拆分后父模块自带的全部渠道：线下人工确认 + 测试渠道。</summary>
    private static IPaymentProvider[] BuiltInProviders =>
    [
        new OfflineProvider(NullLogger<OfflineProvider>.Instance),
        new NullProvider(NullLogger<NullProvider>.Instance)
    ];

    /// <summary>
    /// 父模块自己注册的渠道<b>只剩</b>不依赖任何厂商包的那两个。
    /// </summary>
    /// <remarks>
    /// 上面那份 <see cref="BuiltInProviders"/> 是手写的清单，本用例负责证明它与模块的实际注册一致 ——
    /// 否则「父模块又把某个渠道加回来了」会让手写清单静默过期，而这批用例照样全绿。
    /// 同时它也是拆分本身的门禁：谁把厂商 SDK 重新拖回父模块的依赖闭包，这里立刻红。
    /// </remarks>
    [Fact]
    public async Task PaymentModule_RegistersOnlyTheChannelsThatNeedNoVendorPackage()
    {
        var context = new ServiceConfigurationContext(new ServiceCollection(), new ConfigurationBuilder().Build());

        await new PaymentModule().ConfigureServicesAsync(context);

        var channels = context.Services
            .Where(x => x.ServiceType == typeof(IPaymentProvider))
            .Select(x => x.ImplementationType)
            .ToList();

        channels.ShouldBe([typeof(OfflineProvider), typeof(NullProvider)], ignoreOrder: true);
        BuiltInProviders.Select(x => x.GetType()).ShouldBe(channels, ignoreOrder: true);
    }

    /// <summary>把渠道在配置里开到最足，模拟「配置照着文档写了，就是没引那个包」的现场。</summary>
    private static PaymentProviderFactory CreateFactory(string enabledChannelCode)
    {
        var options = new PaymentOptions
        {
            AllowTestProvider = true,
            Channels = new Dictionary<string, ChannelOptions>
            {
                [enabledChannelCode] = new() { Enabled = true },
                [PaymentConstants.OfflineChannelCode] = new() { Enabled = true }
            }
        };

        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.Setup(x => x.CurrentValue).Returns(options);

        return new PaymentProviderFactory(BuiltInProviders, monitor.Object, NullLogger<PaymentProviderFactory>.Instance);
    }

    [Theory]
    [InlineData(PaymentConstants.StripeChannelCode)]
    [InlineData(PaymentConstants.PayPalChannelCode)]
    public void EnablingTheChannelInConfiguration_DoesNotConjureAProvider(string channelCode)
    {
        var factory = CreateFactory(channelCode);

        factory.GetProvider(channelCode).ShouldBeNull();

        // 自带的渠道不受影响：缺席只减这一个渠道，不牵连别的
        factory.GetProvider(PaymentConstants.OfflineChannelCode).ShouldNotBeNull();
        factory.GetEnabledProviders().Select(x => x.ChannelCode)
            .ShouldNotContain(channelCode, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PaymentConstants.StripeChannelCode)]
    [InlineData(PaymentConstants.PayPalChannelCode)]
    public async Task Webhook_IsRejected_NotSilentlyAcknowledged(string channelCode)
    {
        var service = CreatePaymentService(CreateFactory(channelCode));

        var result = await service.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = channelCode,
            Parameters = new Dictionary<string, string>
            {
                [PaymentConstants.CallbackRawBodyKey] = "{\"id\":\"evt_1\",\"event_type\":\"PAYMENT.CAPTURE.COMPLETED\"}"
            }
        });

        // 失败 + 4xx：渠道后台会显示投递失败并重投，运维能对上号。
        // 一旦这里变成 Succeeded，渠道就不再重投，那笔支付会永远停在处理中。
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentChannelNotSupported);
        result.Code.ShouldBe(400);
    }

    private static PaymentService CreatePaymentService(IPaymentProviderFactory providerFactory)
    {
        var paymentOptions = new Mock<IOptionsMonitor<PaymentOptions>>();
        paymentOptions.Setup(x => x.CurrentValue).Returns(new PaymentOptions());

        var taxOptions = new Mock<IOptionsMonitor<TaxOptions>>();
        taxOptions.Setup(x => x.CurrentValue).Returns(new TaxOptions());

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        return new PaymentService(
            new Mock<IRepository<Tnzi.Payment.Entities.Payment, Guid>>().Object,
            providerFactory,
            new DefaultPaymentTaxCalculator(taxOptions.Object),
            new Mock<IPaymentMethodService>().Object,
            paymentOptions.Object,
            serviceProvider.Object);
    }
}
