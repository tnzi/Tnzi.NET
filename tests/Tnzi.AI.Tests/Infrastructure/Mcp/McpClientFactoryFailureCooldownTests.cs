using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

/// <summary>
/// 一个连不上的 MCP 服务器不能拖住每一次 agent 运行：失败后冷却一段时间、建连按分区键各自加锁、HTTP 初始化有上限。
/// </summary>
/// <remarks>
/// 此前每次运行都对失败的服务器重走整轮重试（3 次 × SDK 默认 60 秒初始化超时），没有任何失败记录；
/// 而且重试期间持有工厂级的全局锁，别的租户、别的服务器的建连与重连全都排在它后面。
/// </remarks>
public class McpClientFactoryFailureCooldownTests
{
    [Fact]
    public async Task GetOrCreateClientAsync_AfterAFailedConnect_FailsFastUntilTheCooldownEnds()
    {
        var clock = new ManualTimeProvider();
        var factory = new ScriptedFactory(clock) { Connect = (_, _) => throw new InvalidOperationException("unreachable") };
        var config = HttpConfig("dead-server");

        await Should.ThrowAsync<InvalidOperationException>(() => factory.GetOrCreateClientAsync(config));
        var attemptsAfterFirstRequest = factory.ConnectAttempts;

        var fastFail = await Should.ThrowAsync<McpServerUnavailableException>(() => factory.GetOrCreateClientAsync(config));
        fastFail.Message.ShouldContain("dead-server");
        factory.ConnectAttempts.ShouldBe(attemptsAfterFirstRequest);

        clock.Advance(McpClientFactory.FailureCooldown + TimeSpan.FromSeconds(1));
        await Should.ThrowAsync<InvalidOperationException>(() => factory.GetOrCreateClientAsync(config));
        factory.ConnectAttempts.ShouldBeGreaterThan(attemptsAfterFirstRequest);
    }

    [Fact]
    public async Task InvalidateClientAsync_ClearsTheCooldown()
    {
        // 注册表改了这台服务器（修好了地址）或 401 恢复要重连：不能还被上一次失败挡着
        var factory = new ScriptedFactory(new ManualTimeProvider()) { Connect = (_, _) => throw new InvalidOperationException("unreachable") };
        var config = HttpConfig("fixed-server");
        await Should.ThrowAsync<InvalidOperationException>(() => factory.GetOrCreateClientAsync(config));

        var healthy = HealthyAdapter();
        factory.Connect = (_, _) => Task.FromResult(healthy);
        await factory.InvalidateClientAsync("fixed-server");

        (await factory.GetOrCreateClientAsync(config)).ShouldBeSameAs(healthy);
    }

    [Fact]
    public async Task GetOrCreateClientAsync_SlowServer_DoesNotBlockConnectingAnotherServer()
    {
        var slowConnectStarted = new TaskCompletionSource();
        var releaseSlow = new TaskCompletionSource<IMcpClientAdapter>();
        var fast = HealthyAdapter();
        var factory = new ScriptedFactory(new ManualTimeProvider())
        {
            Connect = (config, _) =>
            {
                if (config.Name == "slow-server")
                {
                    slowConnectStarted.TrySetResult();
                    return releaseSlow.Task;
                }
                return Task.FromResult(fast);
            }
        };

        var slow = factory.GetOrCreateClientAsync(HttpConfig("slow-server"));
        await slowConnectStarted.Task;

        var other = factory.GetOrCreateClientAsync(HttpConfig("other-server"));
        (await Task.WhenAny(other, Task.Delay(TimeSpan.FromSeconds(5)))).ShouldBeSameAs(other);
        (await other).ShouldBeSameAs(fast);

        releaseSlow.SetResult(HealthyAdapter());
        await slow;
    }

    [Fact]
    public async Task GetOrCreateClientAsync_ConcurrentRequestsForADeadServer_OnlyOneRunsTheRetries()
    {
        var factory = new ScriptedFactory(new ManualTimeProvider()) { Connect = (_, _) => throw new InvalidOperationException("unreachable") };
        var config = HttpConfig("dead-server");

        var requests = Enumerable.Range(0, 4).Select(_ => factory.GetOrCreateClientAsync(config)).ToList();
        var failures = new List<Exception>();
        foreach (var request in requests)
        {
            try
            {
                await request;
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        // 一轮重试（MaxRetries 次尝试）之后，其余排队的请求都命中冷却
        failures.Count.ShouldBe(4);
        failures.OfType<McpServerUnavailableException>().Count().ShouldBe(3);
        factory.ConnectAttempts.ShouldBe(3);
    }

    [Fact]
    public void CreateClientOptions_HttpServer_BoundsTheInitializationTimeout()
    {
        var factory = new ScriptedFactory(new ManualTimeProvider());

        var options = factory.CreateClientOptions(HttpConfig("remote"));

        options.ShouldNotBeNull().InitializationTimeout.ShouldBe(McpClientFactory.HttpInitializationTimeout);
        McpClientFactory.HttpInitializationTimeout.ShouldBeLessThan(TimeSpan.FromSeconds(60));
    }

    private static McpServerConfig HttpConfig(string name) => new()
    {
        Name = name,
        ConnectionType = McpConnectionType.Http,
        Endpoint = "https://mcp.example.com/mcp"
    };

    private static IMcpClientAdapter HealthyAdapter()
    {
        var adapter = new Mock<IMcpClientAdapter>();
        adapter.Setup(a => a.IsConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        adapter.Setup(a => a.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return adapter.Object;
    }

    private sealed class ScriptedFactory(TimeProvider clock)
        : McpClientFactory(NullLoggerFactory.Instance, NullLogger<McpClientFactory>.Instance, timeProvider: clock)
    {
        private int _connectAttempts;

        public Func<McpServerConfig, CancellationToken, Task<IMcpClientAdapter>> Connect { get; set; }
            = (_, _) => throw new InvalidOperationException("No connect behaviour configured");

        public int ConnectAttempts => Volatile.Read(ref _connectAttempts);

        protected internal override Task<IMcpClientAdapter> CreateAndConnectInternalAsync(McpServerConfig config, CancellationToken ct)
        {
            Interlocked.Increment(ref _connectAttempts);
            return Connect(config, ct);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
