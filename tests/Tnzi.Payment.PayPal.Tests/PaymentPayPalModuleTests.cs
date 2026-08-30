namespace Tnzi.Payment.PayPal.Tests;

/// <summary>
/// 拆包引入的那条新接缝：渠道的注册与配置绑定<b>不再</b>由 <c>PaymentModule</c> 完成，改由本模块负责。
/// 这批用例证明它真的做了，而且做出来的东西与拆分前逐字一致。
/// </summary>
/// <remarks>
/// <para>
/// 没有这批用例，「模块忘了注册」的现场是：包引了、配置写得好好的、启动一声不响，
/// 直到第一笔 PayPal 支付才拿到 400 <c>PAYMENT_CHANNEL_NOT_SUPPORTED</c> ——
/// 与「压根没加载这个包」完全一样的症状，而两者该查的地方完全不同。
/// </para>
/// <para>
/// <c>Payment:PayPal</c> 这个配置节路径同理：拆包<b>不改配置面</b>是对既有部署的承诺，
/// 而写错一个字的表现只是「凭据读出来是空的」，看起来像是配置没填。
/// </para>
/// </remarks>
public class PaymentPayPalModuleTests
{
    private static ServiceConfigurationContext ContextFor(IConfiguration configuration)
        => new(new ServiceCollection(), configuration);

    private static IConfiguration EmptyConfiguration => new ConfigurationBuilder().Build();

    [Fact]
    public async Task Module_RegistersThePayPalChannel()
    {
        var context = ContextFor(EmptyConfiguration);

        await new PaymentPayPalModule().ConfigureServicesAsync(context);

        var descriptor = context.Services.ShouldHaveSingleItem();
        descriptor.ServiceType.ShouldBe(typeof(IPaymentProvider));
        descriptor.ImplementationType.ShouldBe(typeof(PayPalProvider));
        // 渠道是多注册服务，工厂按 ChannelCode 分发，所以这里必须是 Add 而不是 TryAdd：
        // TryAdd 会在已有任意一个 IPaymentProvider 时整个跳过，表现为「少了一个渠道」。
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    /// <summary>
    /// 渠道代码必须仍是 <c>PayPal</c>：它同时是工厂的字典键、<c>Payment:Channels</c> 的配置键，
    /// 以及 <c>Payment:DefaultChannelCode</c> 可以取的值。改动它等于让既有部署的配置突然失效。
    /// </summary>
    [Fact]
    public void Provider_KeepsItsChannelCode()
    {
        var provider = new PayPalProvider(
            MsOptions.Create(new PayPalOptions()),
            new Mock<IHttpClientFactory>().Object,
            NullLogger<PayPalProvider>.Instance);

        provider.ChannelCode.ShouldBe(PaymentConstants.PayPalChannelCode);
    }

    /// <summary>
    /// 配置节路径与拆分前逐字一致：<c>Payment:PayPal</c>。换个地方绑定，不换路径。
    /// </summary>
    [Fact]
    public async Task Module_BindsTheUnchangedConfigurationSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:PayPal:ClientId"] = "client-id-from-configuration",
                ["Payment:PayPal:Mode"] = "live",
                ["Payment:PayPal:VaultUsagePattern"] = "SUBSCRIPTION_POSTPAID"
            })
            .Build();

        var context = ContextFor(configuration);
        await new PaymentPayPalModule().PreConfigureServicesAsync(context);

        var options = context.Services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<PayPalOptions>>().Value;

        options.ClientId.ShouldBe("client-id-from-configuration");
        options.Mode.ShouldBe("live");
        options.VaultUsagePattern.ShouldBe("SUBSCRIPTION_POSTPAID");
    }

    /// <summary>
    /// <see cref="PayPalProvider"/> 注入 <c>IHttpClientFactory</c>，而它不是框架的必然注册项。
    /// 这一步随实现从父模块搬来，缺了它整个渠道在解析阶段就炸在 DI 容器里。
    /// </summary>
    [Fact]
    public async Task Module_EnsuresHttpClientFactoryIsAvailable()
    {
        var context = ContextFor(EmptyConfiguration);

        await new PaymentPayPalModule().PostConfigureServicesAsync(context);

        context.Services.BuildServiceProvider()
            .GetService<IHttpClientFactory>().ShouldNotBeNull();
    }

    /// <summary>
    /// 已经有人注册过就不再插手：应用自己的 <c>AddHttpClient(name, configure)</c> 带着具体配置，
    /// 补一份默认注册只会让「我明明配了超时」这类问题变得难查。
    /// </summary>
    [Fact]
    public async Task Module_DoesNotTouchAnExistingHttpClientRegistration()
    {
        var context = ContextFor(EmptyConfiguration);
        context.Services.AddHttpClient();
        var before = context.Services.Count;

        await new PaymentPayPalModule().PostConfigureServicesAsync(context);

        context.Services.Count.ShouldBe(before);
    }
}
