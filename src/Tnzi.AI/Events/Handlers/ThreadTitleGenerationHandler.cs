using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Events.Handlers;

/// <summary>
/// 线程首轮对话完成后自动生成标题。
/// 所有依赖均为可选注入，因为 HostingLite 等场景下 EF Core/AI 服务可能不可用。
/// </summary>
public class ThreadTitleGenerationHandler : IEventHandler<ThreadFirstReplyCompletedEvent>
{
    private readonly IAiUtility? _aiUtility;
    private readonly IRepository<AgentThreadEntity, Guid>? _threadRepository;
    private readonly IOptionsMonitor<ThreadOptions>? _options;
    private readonly ILogger<ThreadTitleGenerationHandler> _logger;

    public ThreadTitleGenerationHandler(
        IAiUtility? aiUtility = null,
        IRepository<AgentThreadEntity, Guid>? threadRepository = null,
        IOptionsMonitor<ThreadOptions>? options = null,
        ILogger<ThreadTitleGenerationHandler>? logger = null)
    {
        _aiUtility = aiUtility;
        _threadRepository = threadRepository;
        _options = options;
        _logger = logger ?? NullLogger<ThreadTitleGenerationHandler>.Instance;
    }

    public async Task HandleAsync(ThreadFirstReplyCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_aiUtility == null || _threadRepository == null || _options == null)
            {
                _logger.LogDebug("Required services not available, skipping AI title generation for {ThreadId}", @event.ThreadId);
                return;
            }

            var threadOptions = _options.CurrentValue;
            if (!threadOptions.AutoGenerateTitle)
                return;

            var thread = await _threadRepository.GetAsync(@event.ThreadId, cancellationToken);
            if (thread == null)
            {
                _logger.LogWarning("Thread not found for title generation: {ThreadId}", @event.ThreadId);
                return;
            }

            // 组合用户消息和 AI 回复作为生成内容
            var content = $"User: {@event.UserMessage}\nAssistant: {@event.AssistantReply}";

            var title = await _aiUtility.GenerateTitleAsync(
                content, threadOptions.TitleMaxLength, cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(title))
            {
                _logger.LogDebug("AI title generation returned empty for thread {ThreadId}", @event.ThreadId);
                return;
            }

            thread.Title = title;
            await _threadRepository.UpdateAsync(thread, cancellationToken);

            _logger.LogDebug("Thread title generated: {ThreadId} -> {Title}", @event.ThreadId, title);
        }
        catch (Exception ex)
        {
            // Silent catch: 标题生成失败不影响主流程
            _logger.LogWarning(ex, "Failed to generate thread title for {ThreadId}", @event.ThreadId);
        }
    }
}
