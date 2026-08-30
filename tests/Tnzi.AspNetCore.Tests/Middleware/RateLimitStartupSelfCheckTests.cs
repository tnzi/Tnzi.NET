using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 启动期自检：限流唯一必然静默失效的配置组合。
/// </summary>
/// <remarks>
/// 刻意<strong>不测私有判定函数而是真的把应用启起来</strong>：这条自检的全部价值在于"它会被调用"，
/// 一个纯函数真值表能在自检从 <c>OnApplicationInitializationAsync</c> 里被删掉之后照样全绿。
/// 附带的好处是"提供者已注册"那条走的是真实 DI 解析（提供者允许是 scoped，从 root 解析会抛）。
/// </remarks>
public class RateLimitStartupSelfCheckTests
{
    /// <summary>告警文本里最稳定的一段，改文案时这里会一起改，不会静默失配。</summary>
    private const string WarningFragment = "cannot take effect for anonymous requests";

    /// <summary>防锈探针文本，见 <see cref="CaptureStartupLogsAsync"/>。</summary>
    private const string ProbeMessage = "rate-limit-self-check-probe";

    [Fact]
    public async Task SelfCheck_WhenIpCollectionOffAndNoProviderAndAllow_ShouldWarnAtStartup()
    {
        var logs = await CaptureStartupLogsAsync(IneffectiveCombination());

        Assert.Contains(logs, m => m.Contains(WarningFragment, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SelfCheck_WhenPartitionKeyProviderRegistered_ShouldStaySilent()
    {
        // 注册成 scoped：自检必须开作用域解析，否则这条会在 root provider 上抛。
        var logs = await CaptureStartupLogsAsync(
            IneffectiveCombination(),
            services => services.AddScoped<IRateLimitPartitionKeyProvider, TicketPartitionKeyProvider>());

        Assert.DoesNotContain(logs, m => m.Contains(WarningFragment, StringComparison.Ordinal));
    }

    [Theory]
    // 拒绝档：匿名请求拿不到分区键会被 429 挡下，限流并未失效。
    [InlineData("AspNetCore:RateLimit:MissingPartitionKey", "Deny")]
    // 全局档：额度是全局桶而非分区，但总量仍有上限，同样不算失效。
    [InlineData("AspNetCore:RateLimit:MissingPartitionKey", "Global")]
    // 仍在采集来源地址：匿名请求还有分区维度。
    [InlineData("AspNetCore:CollectClientIpAddress", "true")]
    // 限流根本没开：不存在"以为它开着"的误解。
    [InlineData("AspNetCore:RateLimit:Enabled", "false")]
    public async Task SelfCheck_WhenAnyLegOfTheCombinationIsAbsent_ShouldStaySilent(string key, string value)
    {
        var settings = IneffectiveCombination();
        settings[key] = value;

        var logs = await CaptureStartupLogsAsync(settings);

        Assert.DoesNotContain(logs, m => m.Contains(WarningFragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// 三条腿齐全的那个组合：限流开着、不采集来源地址、缺分区键时放行。
    /// </summary>
    private static Dictionary<string, string?> IneffectiveCombination() => new()
    {
        ["Database:AutoDiscoverDbContexts"] = "false",
        ["AspNetCore:EnableForwardedHeaders"] = "false",
        ["AspNetCore:RateLimit:Enabled"] = "true",
        ["AspNetCore:CollectClientIpAddress"] = "false"
        // MissingPartitionKey 不写 = 用默认值 Allow，正是要覆盖的那一档。
    };

    private static async Task<List<string>> CaptureStartupLogsAsync(
        Dictionary<string, string?> settings,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);

        var capture = new CapturingLoggerProvider();

        // ★ 注册必须走启动模块的 ConfigureServicesAsync，不能写 builder.Services：
        // TnziApp.CreateAsync 不消费传入 builder 上的服务注册（实测：注册的
        // ILoggerProvider 不在最终容器里），只有模块注册的才进得去。
        RateLimitSelfCheckStartupModule.CaptureFactory = new CapturingLoggerFactory(capture);
        RateLimitSelfCheckStartupModule.ExtraServices = configureServices;

        List<string> logs;
        try
        {
            var app = await TnziApp.CreateAsync<RateLimitSelfCheckStartupModule>(builder);
            await app.StartAsync();

            // 防锈探针：证明捕获管道本身是通的。
            // 没有它，"日志管道没接上"与"自检没有触发"会得出同一个空集合，
            // 于是全部"应当静默"的用例都会假绿（第一版正是这样，探针立刻抓到了）。
            app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("RateLimitSelfCheckProbe")
                .LogWarning(ProbeMessage);

            await app.StopAsync();
            logs = capture.Snapshot();
        }
        finally
        {
            RateLimitSelfCheckStartupModule.CaptureFactory = null;
            RateLimitSelfCheckStartupModule.ExtraServices = null;
        }

        Assert.Contains(logs, m => m.Contains(ProbeMessage, StringComparison.Ordinal));
        return logs;
    }

    private sealed class TicketPartitionKeyProvider : IRateLimitPartitionKeyProvider
    {
        public string? GetPartitionKey(HttpContext context) => "ticket:test";
    }

    private sealed class CapturingLoggerFactory(CapturingLoggerProvider provider) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider loggerProvider)
        {
        }

        public ILogger CreateLogger(string categoryName) => provider.CreateLogger(categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public List<string> Snapshot()
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (sink)
                {
                    sink.Add(formatter(state, exception));
                }
            }
        }
    }
}

/// <summary>
/// 只依赖 AspNetCore 的最小启动模块：自检落在这个模块里，不需要把业务模块一起拉起来。
/// </summary>
/// <remarks>
/// 静态注入点：同一测试类的用例在 xUnit 里串行执行，且 helper 用 finally 复位，故不会互相串味。
/// </remarks>
[DependsOn(typeof(AspNetCoreModule))]
public sealed class RateLimitSelfCheckStartupModule : TnziCustomModule
{
    internal static ILoggerFactory? CaptureFactory;

    internal static Action<IServiceCollection>? ExtraServices;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        if (CaptureFactory != null)
        {
            // ★ 必须替换整个 ILoggerFactory，加 ILoggerProvider 没用：
            // LoggingModule 的 AddSerilog() 把 ILoggerFactory 换成了 SerilogLoggerFactory，
            // 而它的 AddProvider 是空实现，容器里的 ILoggerProvider 一个都不会被问到。
            // 自检取 logger 走的正是 GetRequiredService<ILoggerFactory>()，这里替换即可覆盖它。
            context.Services.RemoveAll<ILoggerFactory>();
            context.Services.AddSingleton(CaptureFactory);
        }

        ExtraServices?.Invoke(context.Services);
        return Task.CompletedTask;
    }
}
