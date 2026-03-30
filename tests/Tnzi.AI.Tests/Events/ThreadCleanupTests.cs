namespace Tnzi.AI.Tests.Events;

/// <summary>
/// ThreadDeletedEvent + ThreadCleanupHandler 测试
/// </summary>
public class ThreadCleanupTests
{
    [Fact]
    public void ThreadDeletedEvent_Properties()
    {
        var threadId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        var evt = new ThreadDeletedEvent
        {
            ThreadId = threadId,
            UserId = userId,
            AgentId = agentId
        };

        evt.ThreadId.ShouldBe(threadId);
        evt.UserId.ShouldBe(userId);
        evt.AgentId.ShouldBe(agentId);
        evt.EventId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void ThreadDeletedEvent_InheritsEventBase()
    {
        var evt = new ThreadDeletedEvent { ThreadId = Guid.NewGuid() };

        evt.ShouldBeAssignableTo<Tnzi.EventBus.EventBase>();
        evt.EventTime.ShouldNotBe(default);
    }

    [Fact]
    public async Task ThreadCleanupHandler_NoRepositories_CompletesWithoutError()
    {
        // All repositories are optional, handler should complete gracefully with none
        var handler = new ThreadCleanupHandler(NullLogger<ThreadCleanupHandler>.Instance);

        var evt = new ThreadDeletedEvent { ThreadId = Guid.NewGuid() };

        // Should not throw
        await handler.HandleAsync(evt);
    }

    [Fact]
    public void ThreadDeletedEvent_TenantId_FromBase()
    {
        // TenantId comes from EventBase, not duplicated in ThreadDeletedEvent
        var tenantId = Guid.NewGuid();
        var evt = new ThreadDeletedEvent
        {
            ThreadId = Guid.NewGuid(),
            TenantId = tenantId  // inherited from EventBase
        };

        evt.TenantId.ShouldBe(tenantId);
    }
}
