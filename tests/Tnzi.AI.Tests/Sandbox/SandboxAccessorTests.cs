using Tnzi.AI.Sandbox.Services;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// F8: explicit sandbox entry point — ISandboxAccessor exposes the active
/// sandbox published by SandboxMiddleware, replacing direct AsyncLocal reads.
/// </summary>
public class SandboxAccessorTests
{
    [Fact]
    public void Current_ReturnsActiveSandbox_WhenEnvironmentPublished()
    {
        var props = new Dictionary<string, object>();
        var ctxAccessor = new Mock<IAgentExecutionContextAccessor>();
        ctxAccessor.Setup(x => x.Properties).Returns(props);
        var sandbox = new Mock<ISandbox>().Object;
        var threadId = Guid.NewGuid();
        props[SandboxPropertyKeys.ToolEnvironment] = new SandboxToolEnvironment(sandbox, threadId);

        var accessor = new SandboxAccessor(ctxAccessor.Object);

        accessor.Current.ShouldBeSameAs(sandbox);
        accessor.CurrentEnvironment!.ThreadId.ShouldBe(threadId);
    }

    [Fact]
    public void Current_ReturnsNull_WhenNoEnvironmentPublished()
    {
        var ctxAccessor = new Mock<IAgentExecutionContextAccessor>();
        ctxAccessor.Setup(x => x.Properties).Returns(new Dictionary<string, object>());

        var accessor = new SandboxAccessor(ctxAccessor.Object);

        accessor.Current.ShouldBeNull();
        accessor.CurrentEnvironment.ShouldBeNull();
    }
}
