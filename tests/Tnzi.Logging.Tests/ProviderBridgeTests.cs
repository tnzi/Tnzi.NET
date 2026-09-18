using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.Logging.EventSource;
using Serilog;
using Serilog.Extensions.Logging;
using Tnzi.Logging.Tests.RequestLogging;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Tnzi.Modules;

namespace Tnzi.Logging.Tests;

/// <summary>
/// Serilog 接管 <c>ILoggerFactory</c> 之后，经 Microsoft.Extensions.Logging 注册的
/// <c>ILoggerProvider</c> 还收不收得到事件。
/// </summary>
/// <remarks>
/// <para>
/// 此前 <see cref="LoggingModule"/> 调的是不带 <c>providers</c> 的 <c>AddSerilog()</c>：
/// <c>SerilogLoggerFactory.AddProvider</c> 是空实现，容器里每一个 <c>ILoggerProvider</c>
/// 都被静默忽略 —— OpenTelemetry 的日志导出器、Application Insights、任何消费方自己注册的
/// provider，一条事件都收不到，而启动照常成功、其它 sink 照常写。
/// </para>
/// <para>
/// 第二条守的是修复本身的副作用：<c>WebApplication.CreateBuilder</c> 预注册了
/// Console / Debug / EventSource / EventLog 四个 provider，桥接之后控制台会被 Serilog 的
/// Console sink 与 MEL 的 ConsoleLoggerProvider 各写一遍。Console 要摘掉（Serilog 已替代它）；
/// Debug / EventLog 也摘掉，但理由不同：模块没有等价 sink，桥接之前它们就一条都没收到过，
/// 保持不投递；EventSource 没有 Serilog 等价物（dotnet-trace / dotnet-monitor 靠它拿日志），要留下。
/// </para>
/// <para>
/// 第三条守的是桥接改变了性质的一种注册：消费方顺手写的 <c>builder.Logging.AddSerilog()</c>
/// 此前是惰性的，桥接后它把事件写回 <c>Log.Logger</c> 形成回路，必须摘掉。
/// </para>
/// 替换的是进程级的 <see cref="Log.Logger"/>，故进 <see cref="SerilogGlobalCollection"/>。
/// </remarks>
[Collection(SerilogGlobalCollection.Name)]
public class ProviderBridgeTests : IDisposable
{
    private readonly Serilog.ILogger _previousLogger = Log.Logger;

    public void Dispose()
    {
        // 模块把自己建的 logger 赋给了进程级 Log.Logger：关掉并还原，别留给下一个测试类。
        Log.CloseAndFlush();
        Log.Logger = _previousLogger;
    }

    [Fact]
    public async Task ProviderRegisteredAfterLoggingModule_ReceivesEvents()
    {
        var probe = new RecordingLoggerProvider();

        await using var provider = await BuildAsync(services =>
            services.AddLogging(b => b.AddProvider(probe)));

        provider.GetRequiredService<ILogger<ProviderBridgeTests>>()
            .LogInformation("bridge-probe {Value}", 42);

        var record = probe.Records.ShouldHaveSingleItem();
        record.Category.ShouldBe(typeof(ProviderBridgeTests).FullName);
        record.Level.ShouldBe(LogLevel.Information);
        record.Message.ShouldContain("bridge-probe 42");
    }

    [Fact]
    public async Task ProviderRegisteredBeforeLoggingModule_AlsoReceivesEvents()
    {
        // 消费方在 Program.cs 里 builder.Logging.AddXxx() 的那种：注册先于模块，同样要收到。
        var probe = new RecordingLoggerProvider();

        await using var provider = await BuildAsync(
            servicesBefore: services => services.AddLogging(b => b.AddProvider(probe)));

        provider.GetRequiredService<ILogger<ProviderBridgeTests>>().LogWarning("early-probe");

        probe.Records.ShouldHaveSingleItem().Message.ShouldBe("early-probe");
    }

    [Fact]
    public async Task SerilogMinimumLevel_StillGatesForwardedEvents()
    {
        // 转发走的是 Serilog 管线：模块的最低级别与来源覆盖在转发前生效，
        // provider 收到的不是「所有事件」而是「Serilog 放行的事件」。
        var probe = new RecordingLoggerProvider();

        await using var provider = await BuildAsync(
            services => services.AddLogging(b => b.AddProvider(probe)),
            configuration: new Dictionary<string, string?> { ["Logging:MinimumLevel"] = "Warning" });

        var logger = provider.GetRequiredService<ILogger<ProviderBridgeTests>>();
        logger.LogInformation("filtered-out");
        logger.LogError("kept");

        probe.Records.ShouldHaveSingleItem().Message.ShouldBe("kept");
    }

    [Fact]
    public async Task HostDefaultProvidersNotBridged_AreRemoved()
    {
        // 模拟 WebApplication.CreateBuilder 的四个默认 provider（同样的服务描述符形态）。
        await using var provider = await BuildAsync(
            servicesBefore: services => services.AddLogging(b =>
            {
                b.AddConsole();
                b.AddDebug();
                b.AddEventSourceLogger();
            }));

        var registered = provider.GetServices<ILoggerProvider>().Select(p => p.GetType()).ToList();

        registered.ShouldNotContain(typeof(ConsoleLoggerProvider));
        registered.ShouldNotContain(typeof(DebugLoggerProvider));
        // EventSource 没有 Serilog 等价物：留下，dotnet-trace 才拿得到日志。
        registered.ShouldContain(typeof(EventSourceLoggerProvider));
    }

    [Fact]
    public async Task ConsumerSerilogProvider_IsNotBridged()
    {
        // 消费方在 Program.cs 里顺手写的 builder.Logging.AddSerilog()：桥接之前这一注册是惰性的
        // （SerilogLoggerFactory 忽略 provider），桥接之后它成了回路的一环 ——
        // 事件 -> Providers sink -> SerilogLoggerProvider -> Log.Logger -> Providers sink ……
        // 第一条日志就栈溢出。它是唯一一个按定义绝不能被桥接的 provider，必须摘掉。
        await using var provider = await BuildAsync(
            servicesBefore: services => services.AddLogging(b => b.AddSerilog()));

        var registered = provider.GetServices<ILoggerProvider>().Select(p => p.GetType()).ToList();

        registered.ShouldNotContain(typeof(SerilogLoggerProvider));
    }

    [Fact]
    public async Task ConsumerSerilogProvider_RegisteredThroughAFactory_IsNotBridgedEither()
    {
        // AddSerilog(dispose: true) 注册的是工厂而不是实例：描述符上没有 ImplementationType，
        // 只按类型精确匹配会把它漏在桥里 —— 漏掉的偏偏是唯一会造成回路的那个。
        await using var provider = await BuildAsync(
            servicesBefore: services => services.AddLogging(b => b.AddSerilog(dispose: true)));

        var registered = provider.GetServices<ILoggerProvider>().Select(p => p.GetType()).ToList();

        registered.ShouldNotContain(typeof(SerilogLoggerProvider));
    }

    [Fact]
    public async Task WithAConsumerSerilogProvider_EventsStillReachOtherProvidersOnce()
    {
        // 回路被切断之后，日志照常经桥到达别的 provider，且只到一次。
        // 这条在修复前不会红而是让整个测试进程栈溢出：回路是真的。
        var probe = new RecordingLoggerProvider();

        await using var provider = await BuildAsync(
            services => services.AddLogging(b => b.AddProvider(probe)),
            servicesBefore: services => services.AddLogging(b => b.AddSerilog()));

        provider.GetRequiredService<ILogger<ProviderBridgeTests>>().LogWarning("loop-probe");

        probe.Records.ShouldHaveSingleItem().Message.ShouldBe("loop-probe");
    }

    /// <summary>
    /// 跑 <see cref="LoggingModule"/> 的 PreConfigure + Configure，前后各留一个注册钩子，
    /// 返回构建好的容器。控制台与文件 sink 都关掉：这里只看 provider 桥接。
    /// </summary>
    private static async Task<ServiceProvider> BuildAsync(
        Action<IServiceCollection>? servicesAfter = null,
        Action<IServiceCollection>? servicesBefore = null,
        Dictionary<string, string?>? configuration = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Logging:EnableConsole"] = "false",
            ["Logging:EnableFile"] = "false"
        };
        foreach (var (key, value) in configuration ?? [])
        {
            settings[key] = value;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        servicesBefore?.Invoke(services);

        var context = new ServiceConfigurationContext(services, config);
        var module = new LoggingModule();
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        servicesAfter?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed record LogRecord(string Category, LogLevel Level, string Message);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogRecord> _records = [];

        public IReadOnlyList<LogRecord> Records
        {
            get
            {
                lock (_records)
                {
                    return [.. _records];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _records);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(string category, List<LogRecord> sink) : ILogger
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
                    sink.Add(new LogRecord(category, logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
