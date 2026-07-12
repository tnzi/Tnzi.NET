namespace Tnzi.AI.Events.Handlers;

/// <summary>
/// AgentPersona 缓存失效处理器 — 监听 AgentPersonaUpdatedEvent / AgentPersonaDeletedEvent，
/// 调用 ContextInjectionMiddleware.InvalidatePersona 立即清除该 Persona 的所有租户缓存条目，
/// 让管理员编辑/删除在所有请求中即时生效，而不必等 5 分钟 TTL 自然过期。
/// </summary>
/// <remarks>
/// 进程内失效。多实例部署下每个 pod 各自订阅 EventBus，触发自身静态字典清理；
/// 如果 EventBus 是单机内存实现 (InMemoryEventBus)，其他 pod 不会收到事件 → 仍依赖
/// 5 分钟 TTL 兜底。生产部署若需跨实例即时失效，应配置分布式 EventBus（如 Kafka/RabbitMQ）。
/// </remarks>
public class AgentPersonaCacheInvalidationHandler
    : IEventHandler<AgentPersonaUpdatedEvent>, IEventHandler<AgentPersonaDeletedEvent>
{
    private readonly ILogger<AgentPersonaCacheInvalidationHandler> _logger;

    public AgentPersonaCacheInvalidationHandler(ILogger<AgentPersonaCacheInvalidationHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public Task HandleAsync(AgentPersonaUpdatedEvent evt, CancellationToken ct = default)
    {
        Check.NotNull(evt);
        // 不再吞异常：失效失败应冒泡给事件总线，由其错误隔离 + 重试 + DLQ 兜底
        ContextInjectionMiddleware.InvalidatePersona(evt.PersonaId);
        _logger.LogDebug("Invalidated persona cache for {PersonaId} (updated)", evt.PersonaId);
        return Task.CompletedTask;
    }

    public Task HandleAsync(AgentPersonaDeletedEvent evt, CancellationToken ct = default)
    {
        Check.NotNull(evt);
        ContextInjectionMiddleware.InvalidatePersona(evt.PersonaId);
        _logger.LogDebug("Invalidated persona cache for {PersonaId} (deleted)", evt.PersonaId);
        return Task.CompletedTask;
    }
}
