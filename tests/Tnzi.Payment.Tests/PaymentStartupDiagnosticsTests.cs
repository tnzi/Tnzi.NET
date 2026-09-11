using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tnzi.Caching;
using Tnzi.Modules;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 启动期诊断：把「这台部署上支付会怎么退化」一次说清，而不是留给运维按请求逐个发现。
/// </summary>
/// <remarks>
/// webhook 去重键写在 <c>ICache</c> 里。框架默认注册的是**进程内**缓存，
/// 多实例部署下每个实例各存一份，重投事件被路由到另一个实例时去重命中不了。
/// 这**不是**正确性问题（状态推进走 CAS + 终态不可改写），但它是一件运维应该知道的事，
/// 而此前没有任何地方说过。<c>ICache</c> 完全没注册则是另一回事：短路整条不存在且毫无迹象。
/// </remarks>
public class PaymentStartupDiagnosticsTests
{
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose() { }

        private sealed class CapturingLogger(List<(LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private static async Task<CapturingLoggerProvider> RunStartupAsync(Action<IServiceCollection> configure, Action<PaymentOptions>? payment = null)
    {
        var services = new ServiceCollection();
        var provider = new CapturingLoggerProvider();
        services.AddLogging(b => b.AddProvider(provider));
        services.AddOptions();
        // 默认：启用内置的线下渠道（它正是出厂默认渠道），让渠道那条诊断保持沉默，只留下缓存这一条
        Action<PaymentOptions> configurePayment = payment
            ?? (o => o.Channels[PaymentConstants.OfflineChannelCode] = new ChannelOptions { Enabled = true });
        services.Configure(configurePayment);
        services.AddScoped<IPaymentProvider, OfflineProvider>();
        services.AddScoped<IPaymentProvider, NullProvider>();
        services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();
        configure(services);

        var module = new PaymentModule();
        await module.OnApplicationInitializationAsync(
            new ApplicationInitializationContext(services.BuildServiceProvider()));

        return provider;
    }

    [Fact]
    public async Task WithNoCacheRegistered_TheLostDeduplicationIsReported()
    {
        var logs = await RunStartupAsync(_ => { });

        var entry = logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("de-duplication is off");
    }

    [Fact]
    public async Task WithTheInProcessCache_TheDeduplicationScopeIsReported()
    {
        var logs = await RunStartupAsync(s =>
        {
            s.AddMemoryCache();
            s.AddSingleton<ICache, MemoryCacheService>();
        });

        var entry = logs.Entries.ShouldHaveSingleItem();
        // Information 而不是 Warning：去重不承载正确性，单实例部署占绝大多数，
        // 在那里报警只会训练运维忽略警告
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain("scoped to this instance");
    }

    [Fact]
    public async Task WithADistributedCache_NothingIsReported()
    {
        var logs = await RunStartupAsync(s => s.AddSingleton<ICache>(new Mock<ICache>().Object));

        logs.Entries.ShouldBeEmpty();
    }

    /// <summary>
    /// 缓存那条诊断不能把渠道那条挤掉：两条各说一件事，此前渠道那条在方法里提前 return。
    /// 这里一个渠道都没启用，但默认值被显式指向 Stripe —— 这不是「还没配」，是打算收款却没加载包，
    /// 必须仍是 Error，并指名那个包。
    /// </summary>
    [Fact]
    public async Task AnUnusableDefaultChannel_IsStillReported()
    {
        var services = new ServiceCollection();
        var provider = new CapturingLoggerProvider();
        services.AddLogging(b => b.AddProvider(provider));
        services.AddOptions();
        services.Configure<PaymentOptions>(o => o.DefaultChannelCode = "Stripe");
        services.AddScoped<IPaymentProvider, OfflineProvider>();
        services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();
        services.AddMemoryCache();
        services.AddSingleton<ICache, MemoryCacheService>();

        await new PaymentModule().OnApplicationInitializationAsync(
            new ApplicationInitializationContext(services.BuildServiceProvider()));

        provider.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("Tnzi.Payment.Stripe"));
        provider.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("scoped to this instance"));
    }

    /// <summary>
    /// 出厂默认渠道必须是本模块自带的那个。它曾是 <c>Stripe</c>，而 Stripe 自 2026-08-29 住在可选子模块里：
    /// 不加载它的应用什么都没配错，却一启动就收到「默认渠道不可用」的 Error。
    /// </summary>
    [Fact]
    public void TheFactoryDefaultChannel_IsOneThisModuleShips()
    {
        PaymentConstants.DefaultPaymentChannel.ShouldBe(PaymentConstants.OfflineChannelCode);
        new PaymentOptions().DefaultChannelCode.ShouldBe(PaymentConstants.OfflineChannelCode);
        PaymentConstants.DefaultPaymentChannel.ShouldNotBe(PaymentConstants.StripeChannelCode);
        PaymentConstants.DefaultPaymentChannel.ShouldNotBe(PaymentConstants.PayPalChannelCode);
    }

    /// <summary>
    /// 一个渠道都没启用、默认值也没动过：支付还没开通，不是配错了。只记一条 Information，
    /// 且不能先被工厂的「渠道未启用」Warning 轰一遍 —— 诊断不走 GetProvider。
    /// </summary>
    [Fact]
    public async Task WithNoChannelConfiguredAtAll_OnlyAnInformationLineIsWritten()
    {
        var logs = await RunStartupAsync(
            s => s.AddSingleton<ICache>(new Mock<ICache>().Object),
            payment: _ => { });

        var entry = logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain("No payment channel is enabled");
        entry.Message.ShouldContain(PaymentConstants.OfflineChannelCode);
        entry.Message.ShouldContain("Tnzi.Payment.Stripe");
    }

    /// <summary>
    /// 有别的渠道启用了，默认渠道却没启用：这台部署打算收款，默认值解析不出来就是配错，Error 并指名开关。
    /// </summary>
    [Fact]
    public async Task WithAnotherChannelEnabledButTheDefaultDisabled_AnErrorNamesTheSwitch()
    {
        var custom = new Mock<IPaymentProvider>();
        custom.SetupGet(x => x.ChannelCode).Returns("Custom");

        var logs = await RunStartupAsync(
            s =>
            {
                s.AddSingleton<ICache>(new Mock<ICache>().Object);
                s.AddScoped<IPaymentProvider>(_ => custom.Object);
            },
            payment: o => o.Channels["Custom"] = new ChannelOptions { Enabled = true });

        var entry = logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Message.ShouldContain("Set Payment:Channels:Offline:Enabled=true");
    }
}
