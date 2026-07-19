using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tnzi.AspNetCore.Extensions;
using Tnzi.Data;

namespace Tnzi.AspNetCore.Tests;

/// <summary>
/// The post-migration startup pipeline: <see cref="IPostMigrationStartupTask"/>
/// runs after migrations (fixing the empty-DB two-boot ordering) and isolates
/// per-task errors so a failing task never blocks startup.
/// </summary>
public class PostMigrationStartupTaskTests
{
    private sealed class RecordingTask : IPostMigrationStartupTask
    {
        public bool Ran { get; private set; }

        public Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            Ran = true;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingTask : IPostMigrationStartupTask
    {
        public Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }

    private static WebApplication BuildApp(Action<IServiceCollection> register)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        register(builder.Services);
        return builder.Build();
    }

    [Fact]
    public async Task Runs_all_registered_tasks()
    {
        var task = new RecordingTask();
        await using var app = BuildApp(s => s.AddSingleton<IPostMigrationStartupTask>(task));

        await app.RunPostMigrationStartupTasksAsync();

        Assert.True(task.Ran);
    }

    [Fact]
    public async Task Isolates_task_errors_and_still_runs_the_others()
    {
        var good = new RecordingTask();
        await using var app = BuildApp(s =>
        {
            s.AddSingleton<IPostMigrationStartupTask>(new ThrowingTask());
            s.AddSingleton<IPostMigrationStartupTask>(good);
        });

        // A throwing task must not bubble up — startup continues.
        await app.RunPostMigrationStartupTasksAsync();

        Assert.True(good.Ran);
    }

    [Fact]
    public async Task Is_a_no_op_when_no_tasks_are_registered()
    {
        await using var app = BuildApp(_ => { });

        await app.RunPostMigrationStartupTasksAsync();
    }
}
