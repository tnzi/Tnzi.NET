using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Tnzi.Modules;
using Tnzi.System.Configuration;

namespace Tnzi.System.Tests.Configuration;

/// <summary>
/// 启动那一刻数据库不可达、之后恢复：首次填充失败后，后台重试读到真实值并发出变更通知，
/// 健康检查 <c>settings</c> 在那之前报告未就绪。此前首次失败只记一条 Warning，应用用 appsettings 的默认值一直跑到重启，
/// 而看上去一切健康。
/// </summary>
public class SettingConfigurationInitialLoadTests
{
    private sealed class CompanyOptions
    {
        public string CompanyName { get; set; } = string.Empty;
    }

    private sealed class Harness
    {
        public required SettingConfigurationSource Source { get; init; }
        public required IConfigurationRoot Root { get; init; }
        public required Mock<IRepository<Setting, Guid>> Repository { get; init; }
        public required ServiceProvider Services { get; init; }

        public SettingConfigurationHealthCheck HealthCheck() => new(
            Source, Services.GetRequiredService<IOptionsMonitor<SettingConfigurationOptions>>());

        public SettingConfigurationInitialLoadService RetryService() => new(
            Source,
            Services.GetRequiredService<IOptionsMonitor<SettingConfigurationOptions>>(),
            Services.GetRequiredService<ILogger<SettingConfigurationInitialLoadService>>());

        public void DatabaseDown() =>
            Repository.Setup(r => r.AsQueryable()).Throws(new TimeoutException("Timeout during reading attempt"));

        public void DatabaseUp(params Setting[] rows) =>
            Repository.Setup(r => r.AsQueryable()).Returns(rows.ToList().BuildMock());
    }

    private static Harness Arrange(Action<SettingConfigurationOptions>? configure = null)
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["System:CompanyName"] = "Law Firm CRM",    // the product's placeholder in appsettings
        });
        configBuilder.AddTnziSettings();
        var source = configBuilder.Sources.OfType<SettingConfigurationSource>().Single();
        var root = configBuilder.Build();

        var repository = new Mock<IRepository<Setting, Guid>>();
        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        services.AddLogging();
        services.Configure<CompanyOptions>(root.GetSection("System"));
        services.Configure<SettingConfigurationOptions>(o =>
        {
            o.InitialLoadRetryDelay = TimeSpan.FromMilliseconds(10);
            o.InitialLoadMaxRetryDelay = TimeSpan.FromMilliseconds(40);
            configure?.Invoke(o);
        });

        return new Harness
        {
            Source = source,
            Root = root,
            Repository = repository,
            Services = services.BuildServiceProvider(),
        };
    }

    private static Setting Global(string key, string value) => new() { Key = key, Value = value, Scope = SettingScope.Global };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task When_the_database_comes_back_the_real_values_replace_the_defaults_and_subscribers_are_notified()
    {
        var harness = Arrange();
        var monitor = harness.Services.GetRequiredService<IOptionsMonitor<CompanyOptions>>();
        var notified = new List<string>();
        using var subscription = monitor.OnChange(o => notified.Add(o.CompanyName));

        harness.DatabaseDown();
        await harness.Source.AttachAsync(harness.Services);

        monitor.CurrentValue.CompanyName.ShouldBe("Law Firm CRM");
        harness.Source.LoadState.ShouldBe(SettingConfigurationLoadState.Failed);
        var whileDown = await harness.HealthCheck().CheckHealthAsync(new HealthCheckContext());
        whileDown.Status.ShouldBe(HealthStatus.Unhealthy);
        whileDown.Exception.ShouldBeOfType<TimeoutException>();

        var retry = harness.RetryService();
        await retry.StartAsync(CancellationToken.None);
        try
        {
            // A few retries fail while the database is still unreachable.
            await WaitUntilAsync(() => harness.Source.Provider!.FailedAttempts >= 3);
            monitor.CurrentValue.CompanyName.ShouldBe("Law Firm CRM");

            harness.DatabaseUp(Global("System:CompanyName", "Smith & Partners LLP"));

            await WaitUntilAsync(() => harness.Source.LoadState == SettingConfigurationLoadState.Loaded);
            await retry.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));    // the service ends once loaded
        }
        finally
        {
            await retry.StopAsync(CancellationToken.None);
        }

        harness.Root["System:CompanyName"].ShouldBe("Smith & Partners LLP");
        monitor.CurrentValue.CompanyName.ShouldBe("Smith & Partners LLP");
        notified.ShouldContain("Smith & Partners LLP");
        harness.Source.Provider!.FailedAttempts.ShouldBe(0);
        (await harness.HealthCheck().CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Once_real_values_were_loaded_a_failed_reload_keeps_them_and_stays_healthy()
    {
        var harness = Arrange();
        harness.DatabaseUp(Global("System:CompanyName", "Smith & Partners LLP"));
        await harness.Source.AttachAsync(harness.Services);

        harness.DatabaseDown();
        await Should.ThrowAsync<TimeoutException>(() => harness.Source.ReloadAsync());

        harness.Source.LoadState.ShouldBe(SettingConfigurationLoadState.Loaded);
        harness.Root["System:CompanyName"].ShouldBe("Smith & Partners LLP");
        (await harness.HealthCheck().CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task A_change_reload_that_succeeds_while_the_retry_waits_also_ends_the_not_loaded_state()
    {
        var harness = Arrange();
        harness.DatabaseDown();
        await harness.Source.AttachAsync(harness.Services);

        harness.DatabaseUp(Global("System:CompanyName", "Smith & Partners LLP"));
        await harness.Source.ReloadAsync();

        harness.Source.LoadState.ShouldBe(SettingConfigurationLoadState.Loaded);
        harness.Root["System:CompanyName"].ShouldBe("Smith & Partners LLP");

        var retry = harness.RetryService();
        await retry.StartAsync(CancellationToken.None);
        await retry.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        harness.Repository.Verify(r => r.AsQueryable(), Times.Exactly(2));    // attach + the change reload, no retry
    }

    [Fact]
    public async Task The_not_loaded_status_is_configurable()
    {
        var harness = Arrange(o => o.NotLoadedHealthStatus = HealthStatus.Degraded);
        harness.DatabaseDown();
        await harness.Source.AttachAsync(harness.Services);

        var result = await harness.HealthCheck().CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Data["failedAttempts"].ShouldBe(1);
    }

    [Fact]
    public async Task A_host_without_a_setting_repository_is_healthy_and_does_not_retry()
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddTnziSettings();
        var source = configBuilder.Sources.OfType<SettingConfigurationSource>().Single();
        configBuilder.Build();
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await source.AttachAsync(services);

        source.LoadState.ShouldBe(SettingConfigurationLoadState.Unavailable);
        var options = new OptionsMonitorStub(new SettingConfigurationOptions());
        (await new SettingConfigurationHealthCheck(source, options).CheckHealthAsync(new HealthCheckContext()))
            .Status.ShouldBe(HealthStatus.Healthy);

        var retry = new SettingConfigurationInitialLoadService(
            source, options, services.GetRequiredService<ILogger<SettingConfigurationInitialLoadService>>());
        await retry.StartAsync(CancellationToken.None);
        await retry.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Stopping_the_host_ends_the_retry_while_the_database_is_still_down()
    {
        var harness = Arrange();
        harness.DatabaseDown();
        await harness.Source.AttachAsync(harness.Services);

        var retry = harness.RetryService();
        await retry.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => harness.Source.Provider!.FailedAttempts >= 2);
        await retry.StopAsync(CancellationToken.None);

        retry.ExecuteTask!.IsCompleted.ShouldBeTrue();
        harness.Source.LoadState.ShouldBe(SettingConfigurationLoadState.Failed);
    }

    [Fact]
    public async Task SystemModule_registers_the_retry_service_and_the_settings_health_check_with_the_source()
    {
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(services, configuration, "Development");
        var module = new SystemModule();

        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        services.ShouldContain(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(SettingConfigurationInitialLoadService));
        using var provider = services.AddLogging().BuildServiceProvider();
        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .ShouldContain(r => r.Name == "settings");
    }

    [Fact]
    public void The_options_validator_rejects_a_maximum_shorter_than_the_first_delay()
    {
        var result = new SettingConfigurationOptionsValidator().Validate(null, new SettingConfigurationOptions
        {
            InitialLoadRetryDelay = TimeSpan.FromSeconds(10),
            InitialLoadMaxRetryDelay = TimeSpan.FromSeconds(5),
        });

        result.Failed.ShouldBeTrue();
    }

    private sealed class OptionsMonitorStub(SettingConfigurationOptions value) : IOptionsMonitor<SettingConfigurationOptions>
    {
        public SettingConfigurationOptions CurrentValue => value;
        public SettingConfigurationOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<SettingConfigurationOptions, string?> listener) => null;
    }
}
