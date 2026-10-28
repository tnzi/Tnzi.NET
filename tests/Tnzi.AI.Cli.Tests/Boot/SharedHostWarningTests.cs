namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 多租户部署开启外部执行时，启动日志如实说明：所有外部运行共用宿主进程账号，运行之间没有隔离。
/// </summary>
/// <remarks>
/// ★ 外部 agent 以 <c>bypassPermissions</c> 跑任意代码，所有运行都以同一个 OS 账号执行：任一 CLI 绑定 Agent 的使用者
/// 都能读到别的租户 / 用户的工作区，以及在途运行写在工作目录里的回写凭据（拿它就能以那个租户的身份调 MCP）。
/// 工作目录与分区只是组织方式，不是权限边界，而文档此前把它写成了边界，部署者据此在多租户下开了它。
/// 这条不是修好了隔离（那要宿主级部署），而是让「不隔离」在它最危险的配置下不再沉默。
/// </remarks>
public class SharedHostWarningTests
{
    private const string WarningFragment = "host process account";

    [Fact]
    public async Task EnabledInAMultiTenantDeployment_WarnsThatRunsAreNotIsolated()
    {
        var logs = await InitializeAsync(cliEnabled: true, multiTenancyEnabled: true);

        logs.ShouldContain(l => l.Level == LogLevel.Warning && l.Message.Contains(WarningFragment));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SingleTenantOrDisabled_DoesNotWarn(bool cliEnabled, bool multiTenancyEnabled)
    {
        var logs = await InitializeAsync(cliEnabled, multiTenancyEnabled);

        logs.ShouldNotContain(l => l.Message.Contains(WarningFragment));
    }

    /// <summary>
    /// 消费方的宿主按用户交出独立目标（容器、OS 账号）时，「共用一个进程账号」这句话不成立，不能照样告警。
    /// </summary>
    [Fact]
    public async Task ARemoteHost_InAMultiTenantDeployment_DoesNotClaimRunsShareTheProcessAccount()
    {
        var remote = TestCliHost.Starting(() => throw new InvalidOperationException("not started"));

        var logs = await InitializeAsync(cliEnabled: true, multiTenancyEnabled: true, remote);

        logs.ShouldNotContain(l => l.Message.Contains(WarningFragment));
        logs.ShouldContain(l => l.Level == LogLevel.Information && l.Message.Contains(nameof(TestCliHost)));
    }

    private static async Task<List<(LogLevel Level, string Message)>> InitializeAsync(
        bool cliEnabled, bool multiTenancyEnabled, ICliHost? host = null)
    {
        var sink = new CapturingLoggerProvider();
        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.GetEnabled()).Returns([]);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(sink));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new CliAgentOptions { Enabled = cliEnabled }));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = multiTenancyEnabled }));
        services.AddSingleton(registry.Object);
        services.AddSingleton(Mock.Of<ICliProtocolAdapterFactory>());
        services.AddSingleton(host ?? LocalHosts.Create());
        await using var provider = services.BuildServiceProvider();

        await new AICliModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));

        return [.. sink.Entries];
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
