using Tnzi.Modules;

namespace Tnzi.AI.Tests;

/// <summary>
/// Shared test helper methods for creating minimal AI middleware contexts
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// 按框架真实的分阶段语义配置模块：先依序执行所有模块的 <c>ConfigureServicesAsync</c>，
    /// 再依序执行所有模块的 <c>PostConfigureServicesAsync</c>（TnziApplication 三阶段中的后两段）。
    /// AIModule 的 NoOp 回退注册在 PostConfigure 阶段 - 任何检查回退/覆盖行为的测试
    /// 必须经由本助手 bootstrap，而不是只调用 ConfigureServicesAsync。
    /// </summary>
    public static void ConfigureModules(IServiceCollection services, IConfiguration configuration, params ITnziModule[] modules)
    {
        var context = new ServiceConfigurationContext(services, configuration);
        foreach (var module in modules)
        {
            module.ConfigureServicesAsync(context).GetAwaiter().GetResult();
        }

        foreach (var module in modules)
        {
            module.PostConfigureServicesAsync(context).GetAwaiter().GetResult();
        }
    }

    public static AiMiddlewareContext CreateMinimalContext(string userMessage = "Hello", Guid? threadId = null)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                UserMessage = userMessage,
                ThreadId = threadId ?? Guid.NewGuid()
            },
            Agent = AgentResolution.Success(
                agent: null!,
                provider: "TestProvider",
                model: "test-model",
                agentId: null),
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Messages = []
        };
    }

    /// <summary>
    /// Creates a lightweight <see cref="IOptionsMonitor{T}"/> stub backed by the supplied value.
    /// Suitable for unit tests that need hot-reload-capable injection without a real DI container.
    /// </summary>
    public static IOptionsMonitor<T> CreateOptionsMonitor<T>(T value) where T : class
    {
        var mock = new Mock<IOptionsMonitor<T>>();
        mock.SetupGet(m => m.CurrentValue).Returns(value);
        return mock.Object;
    }

    /// <summary>
    /// Builds an <see cref="IServiceProvider"/> with an <see cref="IAgentGrantService"/> registered.
    /// The multi-agent execution strategies load a sub-agent's tool groups from grants via the
    /// strategy context's ServiceProvider, so it must resolve a grant service. By default the
    /// projection is empty (no grants) - pass tool groups to grant some.
    /// </summary>
    public static IServiceProvider ServiceProviderWithGrants(params string[] toolGroups)
    {
        var grantService = new Mock<IAgentGrantService>();
        grantService.Setup(s => s.GetGrantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentGrantsProjection { ToolGroups = toolGroups });

        var services = new ServiceCollection();
        services.AddSingleton(grantService.Object);
        return services.BuildServiceProvider();
    }
}
