namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// <c>AI:Cli:Enabled</c> switched on while the host runs takes effect for the background services too.
/// </summary>
/// <remarks>
/// Enqueue, binding and probe endpoints read the current value on every call, but the three background
/// services read it once at startup and returned when it was off. Turning it on without a restart then
/// accepted runs (200 and a run id) that nothing ever claimed: the run stayed Queued, the stream never
/// ended, no runtime was registered, and nothing was logged.
/// </remarks>
public class CliEnabledHotToggleTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task QueueProcessor_StartedDisabled_StartsWorkingOnceEnabled()
    {
        var options = new MutableOptionsMonitor<CliAgentOptions>(Disabled());
        var touched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CliRunQueueProcessor(
            ScopeFactoryThatRecords(typeof(IRepository<CliRun, Guid>), touched),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            options,
            NullLogger<CliRunQueueProcessor>.Instance);

        await AssertIdleUntilEnabledAsync(service, options, touched);
    }

    [Fact]
    public async Task RuntimeProbe_StartedDisabled_StartsProbingOnceEnabled()
    {
        var options = new MutableOptionsMonitor<CliAgentOptions>(Disabled());
        var touched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CliRuntimeProbeService(
            ScopeFactoryThatRecords(typeof(ICliRuntimeService), touched),
            options,
            NullLogger<CliRuntimeProbeService>.Instance);

        await AssertIdleUntilEnabledAsync(service, options, touched);
    }

    [Fact]
    public async Task WorkspaceGc_StartedDisabled_StartsSweepingOnceEnabled()
    {
        var options = new MutableOptionsMonitor<CliAgentOptions>(Disabled());
        var touched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CliWorkspaceGcService(
            ScopeFactoryThatRecords(typeof(IRepository<CliRun, Guid>), touched),
            options,
            NullLogger<CliWorkspaceGcService>.Instance);

        await AssertIdleUntilEnabledAsync(service, options, touched);
    }

    private static async Task AssertIdleUntilEnabledAsync(
        BackgroundService service, MutableOptionsMonitor<CliAgentOptions> options, TaskCompletionSource touched)
    {
        await service.StartAsync(CancellationToken.None);
        try
        {
            // Several poll intervals while disabled: nothing is touched
            await Task.Delay(Poll * 10);
            touched.Task.IsCompleted.ShouldBeFalse("a disabled service must not touch the queue or the runtimes");

            options.CurrentValue = new CliAgentOptions { Enabled = true, PollInterval = Poll };

            var finished = await Task.WhenAny(touched.Task, Task.Delay(Timeout));
            finished.ShouldBe(touched.Task, "the service never started working after the switch was turned on");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static CliAgentOptions Disabled() => new() { Enabled = false, PollInterval = Poll };

    /// <summary>
    /// A scope factory whose providers record the first request for <paramref name="watched"/> and then fail it,
    /// so the service's loop logs the failure and keeps going without any real infrastructure behind it.
    /// </summary>
    private static IServiceScopeFactory ScopeFactoryThatRecords(Type watched, TaskCompletionSource touched)
    {
        var provider = new Mock<IServiceProvider>();
        provider
            .Setup(p => p.GetService(It.IsAny<Type>()))
            .Returns((Type type) =>
            {
                if (type == watched)
                {
                    touched.TrySetResult();
                }

                throw new InvalidOperationException($"No {type.Name} in this test.");
            });

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private sealed class MutableOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; set; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
