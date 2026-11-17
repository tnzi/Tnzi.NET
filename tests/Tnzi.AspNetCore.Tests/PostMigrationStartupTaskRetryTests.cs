using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tnzi.AspNetCore.Extensions;
using Tnzi.AspNetCore.Startup;

namespace Tnzi.AspNetCore.Tests;

/// <summary>
/// 启动那一刻数据库不可达、几分钟后恢复：迁移后启动任务失败不打断启动，在后台重试直到完成，
/// 健康检查 <c>startup-tasks</c> 在那之前报告它们。此前失败的任务只记一条 Error，之后再不运行，
/// 它要修或要播的东西一直缺到下次重启，而应用看上去一切健康。
/// </summary>
public class PostMigrationStartupTaskRetryTests
{
    /// <summary>任务读写的「数据库」：<see cref="Up"/> 为 false 时每次访问都像连接超时那样抛出。</summary>
    private sealed class FlakyDatabase
    {
        private volatile bool _up;

        public bool Up
        {
            get => _up;
            set => _up = value;
        }

        public void Touch()
        {
            if (!_up)
            {
                throw new TimeoutException("Timeout during connecting to the database");
            }
        }
    }

    private sealed class DatabaseTask(FlakyDatabase database, bool required) : IPostMigrationStartupTask
    {
        private int _attempts;
        private int _completions;

        public int Attempts => Volatile.Read(ref _attempts);

        public int Completions => Volatile.Read(ref _completions);

        public bool RequiredForReadiness => required;

        public Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            database.Touch();
            Interlocked.Increment(ref _completions);
            return Task.CompletedTask;
        }
    }

    /// <summary>不碰数据库、总是成功的任务。</summary>
    private sealed class LocalTask : IPostMigrationStartupTask
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _runs);
            return Task.CompletedTask;
        }
    }

    private static WebApplication BuildApp(
        Action<IServiceCollection> register,
        Action<PostMigrationStartupTaskOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddPostMigrationStartupTaskRetry(builder.Configuration);
        builder.Services.Configure<PostMigrationStartupTaskOptions>(o =>
        {
            o.RetryDelay = TimeSpan.FromMilliseconds(10);
            o.MaxRetryDelay = TimeSpan.FromMilliseconds(40);
            configure?.Invoke(o);
        });
        register(builder.Services);

        var app = builder.Build();
        app.MapHealthChecks("/health/ready");
        return app;
    }

    /// <summary>TnziApp 的顺序：先跑启动任务，再启动宿主（后台重试从这里开始）。</summary>
    private static async Task BootAsync(WebApplication app)
    {
        await app.RunPostMigrationStartupTasksAsync();
        await app.StartAsync();
    }

    private static async Task<(HttpStatusCode Status, string Body)> ReadinessAsync(WebApplication app)
    {
        using var response = await app.GetTestClient().GetAsync("/health/ready");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task A_required_task_that_failed_at_startup_completes_once_the_database_is_back_and_readiness_recovers()
    {
        var database = new FlakyDatabase { Up = false };
        var task = new DatabaseTask(database, required: true);
        await using var app = BuildApp(s => s.AddSingleton<IPostMigrationStartupTask>(task));

        // Startup is not blocked by the outage.
        await BootAsync(app);
        var runner = app.Services.GetRequiredService<PostMigrationStartupTaskRunner>();

        var whileDown = await ReadinessAsync(app);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, whileDown.Status);
        Assert.Equal(nameof(HealthStatus.Unhealthy), whileDown.Body);

        // A few background retries fail while the database is still unreachable.
        await WaitUntilAsync(() => task.Attempts >= 3);
        Assert.Equal(0, task.Completions);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await ReadinessAsync(app)).Status);

        database.Up = true;

        await WaitUntilAsync(() => !runner.HasIncomplete);
        Assert.Equal(1, task.Completions);
        var recovered = await ReadinessAsync(app);
        Assert.Equal(HttpStatusCode.OK, recovered.Status);
        Assert.Equal(nameof(HealthStatus.Healthy), recovered.Body);

        // The retry service ends once everything completed; the task is not run again.
        var retry = app.Services.GetServices<IHostedService>().OfType<PostMigrationStartupTaskRetryService>().Single();
        await retry.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        var attempts = task.Attempts;
        await Task.Delay(100);
        Assert.Equal(attempts, task.Attempts);

        await app.StopAsync();
    }

    [Fact]
    public async Task An_optional_task_that_has_not_completed_reports_degraded_without_taking_the_app_out_of_readiness()
    {
        var database = new FlakyDatabase { Up = false };
        var task = new DatabaseTask(database, required: false);
        await using var app = BuildApp(s => s.AddSingleton<IPostMigrationStartupTask>(task));

        await BootAsync(app);

        var whileDown = await ReadinessAsync(app);
        Assert.Equal(HttpStatusCode.OK, whileDown.Status);
        Assert.Equal(nameof(HealthStatus.Degraded), whileDown.Body);

        database.Up = true;
        await WaitUntilAsync(() => task.Completions == 1);
        Assert.Equal(nameof(HealthStatus.Healthy), (await ReadinessAsync(app)).Body);

        await app.StopAsync();
    }

    [Fact]
    public async Task Only_the_tasks_that_failed_are_retried()
    {
        var database = new FlakyDatabase { Up = false };
        var local = new LocalTask();
        var failing = new DatabaseTask(database, required: false);
        await using var app = BuildApp(s =>
        {
            s.AddSingleton<IPostMigrationStartupTask>(local);
            s.AddSingleton<IPostMigrationStartupTask>(failing);
        });

        await BootAsync(app);
        await WaitUntilAsync(() => failing.Attempts >= 3);
        database.Up = true;
        await WaitUntilAsync(() => failing.Completions == 1);

        Assert.Equal(1, local.Runs);
        await app.StopAsync();
    }

    [Fact]
    public async Task A_deployment_can_keep_serving_while_a_required_task_is_retried()
    {
        var database = new FlakyDatabase { Up = false };
        var task = new DatabaseTask(database, required: true);
        await using var app = BuildApp(
            s => s.AddSingleton<IPostMigrationStartupTask>(task),
            o => o.RequiredTaskHealthStatus = HealthStatus.Degraded);

        await BootAsync(app);

        var whileDown = await ReadinessAsync(app);
        Assert.Equal(HttpStatusCode.OK, whileDown.Status);
        Assert.Equal(nameof(HealthStatus.Degraded), whileDown.Body);

        await app.StopAsync();
    }

    [Fact]
    public async Task The_health_check_names_the_incomplete_tasks_and_carries_the_last_failure()
    {
        var database = new FlakyDatabase { Up = false };
        var required = new DatabaseTask(database, required: true);
        await using var app = BuildApp(s => s.AddSingleton<IPostMigrationStartupTask>(required));
        await app.RunPostMigrationStartupTasksAsync();

        var check = new PostMigrationStartupTaskHealthCheck(
            app.Services.GetRequiredService<PostMigrationStartupTaskRunner>(),
            app.Services.GetRequiredService<IOptionsMonitor<PostMigrationStartupTaskOptions>>());
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains(nameof(DatabaseTask), result.Description!);
        Assert.IsType<TimeoutException>(result.Exception);
        Assert.Contains(nameof(DatabaseTask), result.Data.Keys);
    }

    [Fact]
    public async Task Before_the_tasks_have_run_and_after_they_all_completed_the_health_check_is_healthy()
    {
        var local = new LocalTask();
        await using var app = BuildApp(s => s.AddSingleton<IPostMigrationStartupTask>(local));
        var check = new PostMigrationStartupTaskHealthCheck(
            app.Services.GetRequiredService<PostMigrationStartupTaskRunner>(),
            app.Services.GetRequiredService<IOptionsMonitor<PostMigrationStartupTaskOptions>>());

        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);

        await app.RunPostMigrationStartupTasksAsync();

        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(2000, 1000)]
    public void The_options_reject_a_retry_delay_that_is_not_positive_or_longer_than_the_cap(int delayMs, int maxMs)
    {
        var result = new PostMigrationStartupTaskOptionsValidator().Validate(null, new PostMigrationStartupTaskOptions
        {
            RetryDelay = TimeSpan.FromMilliseconds(delayMs),
            MaxRetryDelay = TimeSpan.FromMilliseconds(maxMs),
        });

        Assert.True(result.Failed);
    }
}
