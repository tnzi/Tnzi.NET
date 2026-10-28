using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Events.Handlers;

/// <summary>
/// 线程首轮对话完成后自动生成标题。
/// </summary>
/// <remarks>
/// Runs in an isolated DI scope so that loading and updating the thread does not pollute the
/// originating request's ChangeTracker. If the handler shared the request DbContext, the
/// thread entity it tracked here would race with the main flow's SaveChanges batch - a 0-row
/// UPDATE has been observed in that mode. All dependencies are optional because HostingLite
/// scenarios may not have EF Core or AI services wired up.
/// <para>
/// 后台处理器：标题要再调一次 LLM，同步执行时发布方（AgentRuntime 的首轮收尾）要等它完成，
/// 非流式响应因此多一次完整的 LLM 往返，流式连接在发出 done 之后也要挂到标题生成结束。
/// 它本就在自建作用域里工作、不依赖请求作用域；租户由总线按事件上捕获的 TenantId 恢复。
/// </para>
/// </remarks>
[BackgroundEventHandler]
public class ThreadTitleGenerationHandler : IEventHandler<ThreadFirstReplyCompletedEvent>
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

    public async Task HandleAsync(ThreadFirstReplyCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        // 标题生成落库是持久化副作用，本处理器不吞异常：失败让异常冒泡给事件总线
        // （LocalEventBus 已统一做错误隔离、LogError、重试与死信队列）。此前整个方法体
        // 包 log-only try/catch，会架空总线的重试与 DLQ。
        // 下面的提前返回是对可选依赖/开关的优雅短路（服务缺失、未开启自动标题、线程不存在、
        // AI 返回空标题），并非吞异常。
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var aiUtility = sp.GetService<IAiUtility>();
        var threadRepository = sp.GetService<IRepository<AgentThreadEntity, Guid>>();
        var options = sp.GetService<IOptionsMonitor<ThreadOptions>>();

        if (aiUtility == null || threadRepository == null || options == null)
        {
            _logger.LogDebug("Required services not available, skipping AI title generation for {ThreadId}", @event.ThreadId);
            return;
        }

        var threadOptions = options.CurrentValue;
        if (!threadOptions.AutoGenerateTitle)
            return;

        var thread = await threadRepository.GetAsync(@event.ThreadId, cancellationToken);
        if (thread == null)
        {
            _logger.LogWarning("Thread not found for title generation: {ThreadId}", @event.ThreadId);
            return;
        }

        var content = $"User: {@event.UserMessage}\nAssistant: {@event.AssistantReply}";
        var title = await aiUtility.GenerateTitleAsync(
            content, threadOptions.TitleMaxLength, cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(title))
        {
            _logger.LogDebug("AI title generation returned empty for thread {ThreadId}", @event.ThreadId);
            return;
        }

        thread.Title = title;
        await threadRepository.UpdateAsync(thread, cancellationToken);

        // Persist within the isolated scope so the unit of work does not bleed back into
        // the originating request.
        var uow = sp.GetService<IUnitOfWork>();
        if (uow != null)
        {
            await uow.SaveChangesAsync(cancellationToken);
        }

        _logger.LogDebug("Thread title generated: {ThreadId} -> {Title}", @event.ThreadId, title);
    }
}
