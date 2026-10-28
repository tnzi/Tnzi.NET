using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tnzi.Modules;
using Tnzi.Payment.Options;
using Tnzi.Payment.Services;
using Tnzi.Results;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 每实例的后台扫描开关 <c>Payment:BackgroundTasks:Enabled</c>：几个实例共用一个库时，
/// 只让该跑的那个实例定时扣款、转正、处理逾期。开关只来自本实例的配置，不进共用的设置表。
/// </summary>
public class PaymentBackgroundTaskSwitchTests
{
    [Fact]
    public async Task Disabled_TheLoopEndsAtStartup_RunsNoScan_AndSaysSoOnce()
    {
        var scan = new CountingScan();
        var logger = new RecordingLogger();
        var service = NewService(scan, logger, enabled: false);

        await service.StartAsync(CancellationToken.None);
        var finished = await Task.WhenAny(service.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(5)));

        finished.ShouldBe(service.ExecuteTask, "a disabled instance must not sit in the start-up delay and then scan");
        scan.Runs.ShouldBe(0);
        var line = logger.Entries.ShouldHaveSingleItem();
        line.Level.ShouldBe(LogLevel.Information);
        line.Message.ShouldContain("Payment:BackgroundTasks:Enabled=false");

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ByDefault_TheLoopKeepsRunning_AsBefore()
    {
        var scan = new CountingScan();
        var logger = new RecordingLogger();
        // 不传开关 = 手工构造的旧写法；与配了 Enabled=true 同义
        var service = new PaymentBackgroundService(ProviderWith(scan), logger, Monitor(new PaymentOptions()));

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        service.ExecuteTask!.IsCompleted.ShouldBeFalse("the loop is waiting out its start-up delay, then scans on its interval");
        logger.Entries.ShouldContain(e => e.Message.StartsWith("PaymentBackgroundService started", StringComparison.Ordinal));

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Disabled_ExplicitRunsStillWork()
    {
        // 开关管的是「本实例要不要定时扫」，手工触发 / 测试驱动的一轮照常执行
        var scan = new CountingScan();
        var service = NewService(scan, new RecordingLogger(), enabled: false);

        await service.RunOnceAsync();

        scan.Runs.ShouldBe(1);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task TheSwitch_IsReadFromThisInstancesConfiguration(string? value, bool expected)
    {
        var settings = new Dictionary<string, string?>();
        if (value != null)
            settings["Payment:BackgroundTasks:Enabled"] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var context = new ServiceConfigurationContext(new ServiceCollection(), configuration);

        await new PaymentModule().PreConfigureServicesAsync(context);

        using var provider = context.Services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<PaymentBackgroundTaskOptions>>().Value.Enabled.ShouldBe(expected);
    }

    [Fact]
    public void TheSwitch_IsNotARuntimeSetting()
    {
        // 运行期设置存在各实例共用的设置表里：在开发机上关掉它就是把生产也关掉
        RuntimeSettingMetadataExtractor.Extract(typeof(PaymentBackgroundTaskOptions)).ShouldBeNull();
        RuntimeSettingMetadataExtractor.Extract(typeof(PaymentOptions))!.Fields
            .ShouldNotContain(f => f.Key.StartsWith("Payment:BackgroundTasks", StringComparison.OrdinalIgnoreCase));
    }

    private static PaymentBackgroundService NewService(CountingScan scan, ILogger<PaymentBackgroundService> logger, bool enabled) =>
        new(ProviderWith(scan), logger, Monitor(new PaymentOptions()),
            backgroundTaskOptions: Microsoft.Extensions.Options.Options.Create(new PaymentBackgroundTaskOptions { Enabled = enabled }));

    /// <summary>只放一条贡献式扫描：父模块自己的两条在这里解析不出来，按设计只记 Error、不掀掉这一轮。</summary>
    private static IServiceProvider ProviderWith(CountingScan scan) =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<IPaymentScheduledScan>(scan)
            .BuildServiceProvider();

    private static IOptionsMonitor<PaymentOptions> Monitor(PaymentOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return monitor.Object;
    }

    private sealed class CountingScan : IPaymentScheduledScan
    {
        public int Runs { get; private set; }

        public string Name => "counting";

        public Task<Result<int>> RunAsync(CancellationToken cancellationToken = default)
        {
            Runs++;
            return Task.FromResult(Result.Success(0));
        }
    }

    private sealed class RecordingLogger : ILogger<PaymentBackgroundService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
