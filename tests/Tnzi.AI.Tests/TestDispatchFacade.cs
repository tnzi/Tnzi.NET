namespace Tnzi.AI.Tests;

/// <summary>
/// 把一个 <see cref="IAgentRuntime"/> 测试替身包成 <see cref="IAgentDispatchFacade"/>。
/// </summary>
/// <remarks>
/// 刻意用<b>真实</b>的 <see cref="AgentDispatchFacade"/> + 内建回退绑定服务，而不是再造一个假门面：
/// 这样每一处调 ChatService / AgentService 的既有测试，都顺带断言了「没有外部绑定时
/// 一定走内建路径」——那正是接入路由门面后最需要守住的性质，而它现在<b>免费</b>被
/// 几十个既有用例覆盖着。
/// </remarks>
internal static class TestDispatchFacade
{
    public static IAgentDispatchFacade Wrap(IAgentRuntime runtime)
        => new AgentDispatchFacade(
            runtime,
            new BuiltInOnlyCliAgentBindingService(),
            new NoOpCliAgentDispatcher(),
            // Only reached on the external path, which these tests never take:
            // with no binding the facade hands straight over to the runtime.
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new UnusedThreadService(),
            NullLogger<AgentDispatchFacade>.Instance);

    /// <summary>Only the external path touches threads, and these tests never take it.</summary>
    private sealed class UnusedThreadService : IAgentThreadInternalService
    {
        public Task<(ConversationContext context, Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(
            Guid? threadId, Guid? agentId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Guid> SaveMessageAsync(Guid threadId, string role, string content,
            string? toolCalls = null, string? usage = null, Guid? messageId = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SaveThreadSerializedDataAsync(Guid threadId, ConversationContext context, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
