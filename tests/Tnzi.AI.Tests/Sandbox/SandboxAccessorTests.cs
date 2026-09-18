using Tnzi.AI.Sandbox.Services;
using static Tnzi.AI.Tests.Sandbox.SandboxTestSupport;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// F8: explicit sandbox entry point - ISandboxAccessor exposes the environment
/// published by SandboxMiddleware, replacing direct AsyncLocal reads. The sandbox
/// itself is created on demand, so the accessor's entry point is async.
/// </summary>
public class SandboxAccessorTests
{
    [Fact]
    public async Task GetCurrentAsync_ReturnsTheSandbox_WhenEnvironmentPublished()
    {
        var props = new Dictionary<string, object>();
        var ctxAccessor = new Mock<IAgentExecutionContextAccessor>();
        ctxAccessor.Setup(x => x.Properties).Returns(props);
        var sandbox = new Mock<ISandbox>().Object;
        var threadId = Guid.NewGuid();
        props[SandboxPropertyKeys.ToolEnvironment] = new SandboxToolEnvironment(sandbox, threadId);

        var accessor = new SandboxAccessor(ctxAccessor.Object);

        (await accessor.GetCurrentAsync()).ShouldBeSameAs(sandbox);
        accessor.CurrentEnvironment!.ThreadId.ShouldBe(threadId);
    }

    [Fact]
    public async Task GetCurrentAsync_CreatesTheSandboxOnDemand()
    {
        var props = new Dictionary<string, object>();
        var ctxAccessor = new Mock<IAgentExecutionContextAccessor>();
        ctxAccessor.Setup(x => x.Properties).Returns(props);
        var provider = new CountingSandboxProvider();
        var threadId = Guid.NewGuid();
        props[SandboxPropertyKeys.ToolEnvironment] = new SandboxToolEnvironment(threadId,
            ct => provider.CreateAsync(new SandboxCreateOptions { ThreadId = threadId }, ct));

        var accessor = new SandboxAccessor(ctxAccessor.Object);
        accessor.CurrentEnvironment!.IsSandboxCreated.ShouldBeFalse();

        var sandbox = await accessor.GetCurrentAsync();

        sandbox.ShouldNotBeNull();
        provider.CreateCalls.ShouldBe(1);
        accessor.CurrentEnvironment!.IsSandboxCreated.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsNull_WhenNoEnvironmentPublished()
    {
        var ctxAccessor = new Mock<IAgentExecutionContextAccessor>();
        ctxAccessor.Setup(x => x.Properties).Returns(new Dictionary<string, object>());

        var accessor = new SandboxAccessor(ctxAccessor.Object);

        (await accessor.GetCurrentAsync()).ShouldBeNull();
        accessor.CurrentEnvironment.ShouldBeNull();
    }
}
