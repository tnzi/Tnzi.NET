using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Services;

/// <summary>
/// <see cref="IThreadTitleService"/> 的默认实现。
/// </summary>
/// <remarks>
/// <para>
/// <b>每次写标题都是一条带来源条件的 <c>UPDATE</c></b>，不经变更跟踪器：
/// 回退标题只覆盖占位标题，AI 标题只覆盖「生成中」，用户改名（<see cref="ThreadTitleSource.User"/>）之后两者都匹配不到行。
/// 改名与后台生成同时发生时，数据库按到达顺序裁决，改名不会被一个稍晚写回的 AI 标题盖掉。
/// </para>
/// <para>
/// <b>标题调用照常计量</b>：用量日志按租户 / Agent / 线程记一行（操作类型 <see cref="AIOperationType.ThreadTitle"/>，
/// 租户预算与按 Agent 的预算都会算进去），费用再结算进发起用户的用量窗口。它不经中间件管线，
/// 所以 <c>UsageLoggingMiddleware</c> 与 <c>QuotaMiddleware</c> 都不会再记它一次。
/// 结算不先「开闸」：这一轮在被接受时已经过了用户的用量上限，标题是这一轮的一部分。
/// </para>
/// </remarks>
public class ThreadTitleService : IThreadTitleService
{
    /// <summary>标题输出预算的下界（模型偶尔先吐一点前缀）。</summary>
    private const int MinTitleMaxTokens = 32;

    /// <summary>标题输出预算的上界（再长也不是标题了）。</summary>
    private const int MaxTitleMaxTokens = 256;

    /// <summary>
    /// 标题之外留给 <c>&lt;title&gt;</c> 标签与模型偶尔先吐的一句复述的预算。复述被截断时 FinishReason 是 Length，
    /// 标题就落空了，所以这份余量要装得下一句话。
    /// </summary>
    private const int TitleEnvelopeTokens = 64;

    private readonly IRepository<AgentThreadEntity, Guid> _threads;
    private readonly IOptionsMonitor<ThreadOptions> _options;
    private readonly ILogger<ThreadTitleService> _logger;
    private readonly IEventBus? _eventBus;
    private readonly IAiUtility? _aiUtility;
    private readonly IUsageLogService? _usageLog;
    private readonly IUsageLimitService? _usageLimits;
    private readonly ICostCalculator? _costCalculator;

    public ThreadTitleService(
        IRepository<AgentThreadEntity, Guid> threads,
        IOptionsMonitor<ThreadOptions> options,
        ILogger<ThreadTitleService> logger,
        IEventBus? eventBus = null,
        IAiUtility? aiUtility = null,
        IUsageLogService? usageLog = null,
        IUsageLimitService? usageLimits = null,
        ICostCalculator? costCalculator = null)
    {
        _threads = Check.NotNull(threads);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _eventBus = eventBus;
        _aiUtility = aiUtility;
        _usageLog = usageLog;
        _usageLimits = usageLimits;
        _costCalculator = costCalculator;
    }

    /// <inheritdoc />
    public async Task StartAsync(ThreadTitleRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        try
        {
            var options = _options.CurrentValue;
            var fallback = ThreadTitleText.Fallback(
                request.UserMessage, options.TitleTargetWords, options.TitleTargetCjkCharacters, options.TitleMaxLength);
            if (fallback is null)
            {
                return;
            }

            var generate = options.AutoGenerateTitle && _aiUtility is { IsAvailable: true } && _eventBus is not null;
            var source = generate ? ThreadTitleSource.Generating : ThreadTitleSource.Fallback;

            // 延迟保存的宿主里新线程那一行可能还停在变更跟踪器里：先 flush，再让裸 UPDATE 加入同一个事务
            await _threads.SaveChangesAsync(cancellationToken);
            await _threads.EnsureTransactionStartedAsync(cancellationToken);

            var written = await _threads.AsQueryable()
                .Where(t => t.Id == request.ThreadId && t.TitleSource == ThreadTitleSource.Placeholder)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Title, fallback)
                    .SetProperty(t => t.TitleSource, source), cancellationToken);

            if (written == 0 || !generate)
            {
                return;
            }

            // 总线是事务感知的：调用方开着工作单元事务时，事件推迟到提交之后，后台处理器那时才看得到这一行
            await _eventBus!.PublishAsync(new ThreadTitleRequestedEvent
            {
                ThreadId = request.ThreadId,
                UserMessage = ThreadTitleText.TruncateUtf8(request.UserMessage, options.TitleMaxInputBytes),
                UserId = request.UserId,
                AgentId = request.AgentId
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 标题是附带的：写不下去不能让这一轮失败
            _logger.LogWarning(ex, "Could not title new thread {ThreadId}", request.ThreadId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> GenerateAsync(ThreadTitleRequestedEvent request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var options = _options.CurrentValue;
        var title = options.AutoGenerateTitle && _aiUtility is not null && !string.IsNullOrWhiteSpace(request.UserMessage)
            ? await AskModelAsync(request, options, cancellationToken)
            : null;

        if (title is null)
        {
            // 留着回退标题，告诉客户端标题不会再变了
            await _threads.AsQueryable()
                .Where(t => t.Id == request.ThreadId && t.TitleSource == ThreadTitleSource.Generating)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.TitleSource, ThreadTitleSource.Fallback), cancellationToken);
            return false;
        }

        var written = await _threads.AsQueryable()
            .Where(t => t.Id == request.ThreadId && t.TitleSource == ThreadTitleSource.Generating)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Title, title)
                .SetProperty(t => t.TitleSource, ThreadTitleSource.Generated), cancellationToken);

        if (written == 0)
        {
            _logger.LogDebug("Thread {ThreadId} was renamed or removed while its title was generated; keeping it as is", request.ThreadId);
        }

        return written > 0;
    }

    /// <summary>调一次标题模型并清洗输出；任何不能当标题用的结果都返回 null。</summary>
    private async Task<string?> AskModelAsync(ThreadTitleRequestedEvent request, ThreadOptions options, CancellationToken cancellationToken)
    {
        var callOptions = new AiUtilityCallOptions
        {
            Model = string.IsNullOrWhiteSpace(options.TitleModel) ? null : options.TitleModel.Trim(),
            // 每个字符按最坏情况算一个 token、再翻一倍留开销，另加标签与一句复述的余量
            MaxTokens = Math.Clamp(options.TitleMaxLength * 2 + TitleEnvelopeTokens, MinTitleMaxTokens, MaxTitleMaxTokens)
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await _aiUtility!.ExecuteForResponseAsync(
            ThreadTitleText.BuildSystemPrompt(options.TitleTargetWords, options.TitleTargetCjkCharacters),
            ThreadTitleText.BuildUserMessage(request.UserMessage),
            callOptions,
            cancellationToken);
        stopwatch.Stop();

        if (response is null)
        {
            _logger.LogDebug("Title model gave no answer for thread {ThreadId}; keeping the fallback title", request.ThreadId);
            return null;
        }

        await MeterAsync(request, response, callOptions.Model, stopwatch.ElapsedMilliseconds);

        if (response.FinishReason is AiUtilityFinishReason.Length or AiUtilityFinishReason.ToolCalls or AiUtilityFinishReason.ContentFilter)
        {
            _logger.LogDebug(
                "Title model stopped with {FinishReason} for thread {ThreadId}; keeping the fallback title",
                response.FinishReason, request.ThreadId);
            return null;
        }

        return ThreadTitleText.Clean(response.Text, options.TitleMaxLength);
    }

    /// <summary>
    /// 记一行用量日志并把费用结算进发起用户的用量窗口。计量失败只记日志：标题照样写。
    /// </summary>
    private async Task MeterAsync(ThreadTitleRequestedEvent request, AiUtilityResponse response, string? requestedModel, long durationMs)
    {
        var input = response.InputTokens ?? 0;
        var output = response.OutputTokens ?? 0;
        if (input + output == 0)
        {
            // 实现报不出用量（核心的默认包装、自定义实现）：没有可记的
            return;
        }

        var provider = response.Provider ?? string.Empty;
        var model = response.Model ?? requestedModel ?? string.Empty;
        var cached = response.CachedInputTokens ?? 0;
        var cost = _costCalculator?.CalculateCost(provider, model, input, output, cached, cacheWriteTokens: 0);

        try
        {
            if (_usageLog is not null)
            {
                await _usageLog.LogUsageAsync(new UsageLogEntry
                {
                    OperationType = AIOperationType.ThreadTitle, Provider = provider, Model = model,
                    InputTokens = input, OutputTokens = output, CachedInputTokens = cached,
                    DurationMs = durationMs, IsSuccess = true, EstimatedCostUsd = cost,
                    UserId = request.UserId, AgentId = request.AgentId, ThreadId = request.ThreadId
                }, CancellationToken.None);
            }

            if (_usageLimits is not null && request.UserId is { } userId && cost is > 0m)
            {
                var settled = await _usageLimits.SettleAsync(
                    new UsageLimitReservation(userId, DateTime.UtcNow), cost.Value, CancellationToken.None);
                if (!settled.Succeeded)
                {
                    _logger.LogWarning("Could not record the title cost of thread {ThreadId} for user {UserId}: {Message}",
                        request.ThreadId, userId, settled.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record the usage of the title call for thread {ThreadId}", request.ThreadId);
        }
    }
}
