namespace Tnzi.AI.Guardrails;

/// <summary>
/// 共享 Guardrail 拦截事件发布逻辑（静默失败），供 Input/Output 中间件复用。
/// </summary>
internal static class GuardrailEventPublisher
{
    /// <summary>发布 Guardrail 拦截事件（静默失败）</summary>
    public static async Task PublishGuardrailRejectionEventAsync(
        IEventBus? eventBus,
        ILogger logger,
        Guid? userId,
        Guid? threadId,
        string guardrailName,
        string reason,
        string direction,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (eventBus == null) return;

            await eventBus.PublishAsync(new GuardrailRejectionEvent
            {
                UserId = userId,
                ThreadId = threadId,
                GuardrailName = guardrailName,
                Reason = reason,
                Direction = direction
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish GuardrailRejectionEvent");
        }
    }
}
