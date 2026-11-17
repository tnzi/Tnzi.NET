namespace Tnzi.AI.Events.Handlers;

/// <summary>
/// 后台生成新线程的 AI 标题。
/// </summary>
/// <remarks>
/// 后台处理器：发布方（第一条消息被接受的那一刻）不等它，对话照常进行；标题调用与回复同时跑。
/// 在自建作用域里工作，不碰发起请求的变更跟踪器；租户由总线按事件上捕获的 TenantId 恢复。
/// 写库失败让异常冒泡给总线（重试与死信由总线负责），模型失败不算异常：保留回退标题。
/// </remarks>
[BackgroundEventHandler]
public class ThreadTitleGenerationHandler : IEventHandler<ThreadTitleRequestedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ThreadTitleGenerationHandler> _logger;

    public ThreadTitleGenerationHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<ThreadTitleGenerationHandler>? logger = null)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _logger = logger ?? NullLogger<ThreadTitleGenerationHandler>.Instance;
    }

    public async Task HandleAsync(ThreadTitleRequestedEvent @event, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var titles = scope.ServiceProvider.GetService<IThreadTitleService>();
        if (titles is null)
        {
            _logger.LogDebug("No thread title service; skipping AI title for {ThreadId}", @event.ThreadId);
            return;
        }

        var written = await titles.GenerateAsync(@event, cancellationToken);
        _logger.LogDebug("AI title for thread {ThreadId}: {Outcome}", @event.ThreadId, written ? "written" : "kept the fallback");
    }
}
