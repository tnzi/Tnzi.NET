namespace Tnzi.AI.Infrastructure;

public class EventPublisher : IEventPublisher
{
    private readonly IEventBus? _eventBus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventPublisher> _logger;
    private readonly bool _eventBusAvailable;

    public EventPublisher(IEventBus? eventBus, IServiceScopeFactory scopeFactory, ILogger<EventPublisher> logger)
    {
        _eventBus = eventBus;
        _scopeFactory = Check.NotNull(scopeFactory);
        _logger = Check.NotNull(logger);
        _eventBusAvailable = eventBus != null;
    }

    public async Task PublishRunStartedEventAsync(AgentRunRequest request, AgentRun? run, bool isStreaming,
        string? provider, string? model, AgentExecutionMode executionMode)
    {
        if (!_eventBusAvailable) return;

        try
        {
            await _eventBus!.PublishAsync(new AgentRunStartedEvent
            {
                RunId = run?.Id,
                AgentId = request.AgentId,
                UserId = request.UserId,
                ThreadId = request.ThreadId,
                Provider = provider,
                Model = model,
                IsStreaming = isStreaming,
                ExecutionMode = executionMode.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AgentRunStartedEvent");
        }
    }

    public async Task PublishRunCompletedEventAsync(AgentRunRequest request, AgentRunResult result, AgentRun? run,
        long durationMs, bool isStreaming, string? actualProvider)
    {
        if (!_eventBusAvailable) return;

        try
        {
            await _eventBus!.PublishAsync(new AgentRunCompletedEvent
            {
                RunId = run?.Id ?? result.RunId,
                ThreadId = result.ThreadId ?? request.ThreadId,
                AgentId = request.AgentId,
                UserId = request.UserId,
                Provider = actualProvider ?? result.Provider ?? request.Provider,
                Model = result.Model ?? request.Model,
                TotalTokens = (result.Usage?.InputTokens ?? 0) + (result.Usage?.OutputTokens ?? 0),
                DurationMs = durationMs,
                Status = (run?.Status ?? result.Status ?? AgentRunStatusResolver.Resolve(result.FinishReason)).ToString(),
                FinishReason = result.FinishReason,
                IsStreaming = isStreaming
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AgentRunCompletedEvent");
        }
    }

    public async Task PublishRunFailedEventAsync(AgentRunRequest request, AgentRun? run, Exception exception,
        long durationMs, bool isStreaming)
    {
        if (!_eventBusAvailable) return;

        try
        {
            await _eventBus!.PublishAsync(new AgentRunFailedEvent
            {
                RunId = run?.Id,
                AgentId = request.AgentId,
                UserId = request.UserId,
                ThreadId = request.ThreadId,
                ErrorMessage = exception.Message,
                ExceptionType = exception.GetType().Name,
                DurationMs = durationMs,
                IsStreaming = isStreaming
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AgentRunFailedEvent");
        }
    }

    public async Task HandleNewThreadTitleAsync(AgentRunRequest request, AgentRunResult result)
    {
        if (!request.ThreadId.HasValue) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var threadOptions = sp.GetService<IOptionsMonitor<ThreadOptions>>();
            var maxLength = threadOptions?.CurrentValue.TitleMaxLength ?? 50;
            var fallbackTitle = AgentThreadService.GenerateFallbackTitle(request.UserMessage, maxLength);
            if (fallbackTitle != null)
            {
                var threadService = sp.GetService<IAgentThreadService>();
                if (threadService != null)
                {
                    await threadService.UpdateTitleAsync(request.ThreadId.Value, fallbackTitle);
                }
            }

            var eventBus = sp.GetService<IEventBus>();
            if (eventBus != null && !string.IsNullOrWhiteSpace(request.UserMessage))
            {
                await eventBus.PublishAsync(new ThreadFirstReplyCompletedEvent
                {
                    ThreadId = request.ThreadId.Value,
                    UserMessage = request.UserMessage,
                    AssistantReply = StringTruncator.Truncate(result.Response, 500)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle new thread title for Thread {ThreadId}", request.ThreadId);
        }
    }
}
