using static Tnzi.AI.Tests.Sandbox.SandboxTestSupport;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// 按需创建的沙箱环境：只创建一次、并发只创建一次、失败不缓存、释放后不可再取。
/// </summary>
public class SandboxToolEnvironmentTests
{
    [Fact]
    public async Task GetSandboxAsync_CreatesOnce_AndReusesTheInstance()
    {
        var provider = new CountingSandboxProvider();
        var threadId = Guid.NewGuid();
        await using var environment = new SandboxToolEnvironment(threadId,
            ct => provider.CreateAsync(new SandboxCreateOptions { ThreadId = threadId }, ct));

        Assert.False(environment.IsSandboxCreated);
        var first = await environment.GetSandboxAsync();
        var second = await environment.GetSandboxAsync();

        Assert.Same(first, second);
        Assert.True(environment.IsSandboxCreated);
        Assert.Equal(1, provider.CreateCalls);
    }

    /// <summary>
    /// <c>ls</c> / <c>read_file</c> 标了 IsConcurrencySafe，执行器会并行调用：两个并发的首次调用只能建出一个沙箱。
    /// </summary>
    [Fact]
    public async Task GetSandboxAsync_ConcurrentFirstCalls_CreateExactlyOneSandbox()
    {
        var provider = new CountingSandboxProvider();
        var gate = new TaskCompletionSource();
        var threadId = Guid.NewGuid();
        await using var environment = new SandboxToolEnvironment(threadId, async ct =>
        {
            await gate.Task.WaitAsync(ct);
            return await provider.CreateAsync(new SandboxCreateOptions { ThreadId = threadId }, ct);
        });

        var callers = Enumerable.Range(0, 8).Select(_ => environment.GetSandboxAsync().AsTask()).ToArray();
        gate.SetResult();
        var sandboxes = await Task.WhenAll(callers);

        Assert.Equal(1, provider.CreateCalls);
        Assert.All(sandboxes, s => Assert.Same(sandboxes[0], s));
    }

    /// <summary>
    /// 创建失败不缓存：一次 Docker 抖动或一次被守卫拒绝不该毁掉整轮运行里之后的每一次调用。
    /// </summary>
    [Fact]
    public async Task GetSandboxAsync_FactoryFailure_PropagatesAndRetriesNextTime()
    {
        var provider = new CountingSandboxProvider();
        var failuresLeft = 1;
        provider.FailWith = _ => failuresLeft-- > 0 ? new InvalidOperationException("provider refused") : null;
        var threadId = Guid.NewGuid();
        await using var environment = new SandboxToolEnvironment(threadId,
            ct => provider.CreateAsync(new SandboxCreateOptions { ThreadId = threadId }, ct));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await environment.GetSandboxAsync());
        Assert.Equal("provider refused", failure.Message);
        Assert.False(environment.IsSandboxCreated);

        var sandbox = await environment.GetSandboxAsync();

        Assert.NotNull(sandbox);
        Assert.Equal(2, provider.CreateCalls);
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheCreatedSandbox_AndRefusesFurtherUse()
    {
        var provider = new CountingSandboxProvider();
        var threadId = Guid.NewGuid();
        var environment = new SandboxToolEnvironment(threadId,
            ct => provider.CreateAsync(new SandboxCreateOptions { ThreadId = threadId }, ct));
        await environment.GetSandboxAsync();

        await environment.DisposeAsync();

        Assert.True(provider.Created.Single().Disposed);
        Assert.False(environment.IsSandboxCreated);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await environment.GetSandboxAsync());
    }

    [Fact]
    public async Task DisposeAsync_NeverCreated_DoesNotCallTheFactory()
    {
        var provider = new CountingSandboxProvider();
        var threadId = Guid.NewGuid();
        var environment = new SandboxToolEnvironment(threadId,
            ct => provider.CreateAsync(new SandboxCreateOptions { ThreadId = threadId }, ct));

        await environment.DisposeAsync();
        await environment.DisposeAsync();

        Assert.Equal(0, provider.CreateCalls);
    }

    [Fact]
    public async Task EagerConstructor_ExposesTheGivenSandbox_AndOwnsItsDisposal()
    {
        var sandbox = new CountingSandbox("eager", "unused");
        var threadId = Guid.NewGuid();
        var environment = new SandboxToolEnvironment(sandbox, threadId);

        Assert.True(environment.IsSandboxCreated);
        Assert.Equal(threadId, environment.ThreadId);
        Assert.Same(sandbox, await environment.GetSandboxAsync());

        await environment.DisposeAsync();
        Assert.True(sandbox.Disposed);
    }
}
