
namespace Tnzi.AI.Services;

/// <summary>
/// Usage log service implementation.
/// </summary>
public class UsageLogService : ApplicationService, IUsageLogService
{
    private readonly IRepository<UsageLog, Guid> _repository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICostCalculator? _costCalculator;
    private readonly IRepository<AgentThread, Guid>? _threadRepository;

    public UsageLogService(
        IRepository<UsageLog, Guid> repository,
        IHttpContextAccessor httpContextAccessor,
        IServiceProvider serviceProvider,
        ICostCalculator? costCalculator = null,
        IRepository<AgentThread, Guid>? threadRepository = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _httpContextAccessor = Check.NotNull(httpContextAccessor);
        _costCalculator = costCalculator;
        _threadRepository = threadRepository;
    }

    public Task LogUsageAsync(
        string operationType,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        long durationMs,
        bool isSuccess,
        string? errorMessage = null,
        Guid? agentId = null,
        Guid? threadId = null,
        CancellationToken ct = default)
        => LogUsageCoreAsync(new UsageLogEntry
        {
            OperationType = operationType, Provider = provider, Model = model,
            InputTokens = inputTokens, OutputTokens = outputTokens, DurationMs = durationMs,
            IsSuccess = isSuccess, ErrorMessage = errorMessage, AgentId = agentId, ThreadId = threadId
        }, _costCalculator?.CalculateCost(provider, model, inputTokens, outputTokens), ct);

    public Task LogUsageAsync(
        string operationType,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        long durationMs,
        bool isSuccess,
        string? errorMessage,
        Guid? agentId,
        Guid? threadId,
        decimal? estimatedCostUsd,
        CancellationToken ct = default)
        => LogUsageCoreAsync(new UsageLogEntry
        {
            OperationType = operationType, Provider = provider, Model = model,
            InputTokens = inputTokens, OutputTokens = outputTokens, DurationMs = durationMs,
            IsSuccess = isSuccess, ErrorMessage = errorMessage, AgentId = agentId, ThreadId = threadId,
            EstimatedCostUsd = estimatedCostUsd
        }, estimatedCostUsd ?? _costCalculator?.CalculateCost(provider, model, inputTokens, outputTokens), ct);

    public Task LogUsageAsync(
        string operationType,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        long durationMs,
        bool isSuccess,
        string? errorMessage,
        Guid? agentId,
        Guid? threadId,
        int cachedInputTokens,
        int cacheCreationTokens,
        CancellationToken ct = default)
        => LogUsageCoreAsync(new UsageLogEntry
        {
            OperationType = operationType, Provider = provider, Model = model,
            InputTokens = inputTokens, OutputTokens = outputTokens, DurationMs = durationMs,
            IsSuccess = isSuccess, ErrorMessage = errorMessage, AgentId = agentId, ThreadId = threadId,
            CachedInputTokens = cachedInputTokens, CacheCreationTokens = cacheCreationTokens
        }, _costCalculator?.CalculateCost(provider, model, inputTokens, outputTokens, cachedInputTokens, cacheCreationTokens), ct);

    public Task LogUsageAsync(
        string operationType,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        long durationMs,
        bool isSuccess,
        string? errorMessage,
        Guid? agentId,
        Guid? threadId,
        decimal? estimatedCostUsd,
        int cachedInputTokens,
        int cacheCreationTokens,
        CancellationToken ct = default)
        => LogUsageCoreAsync(new UsageLogEntry
        {
            OperationType = operationType, Provider = provider, Model = model,
            InputTokens = inputTokens, OutputTokens = outputTokens, DurationMs = durationMs,
            IsSuccess = isSuccess, ErrorMessage = errorMessage, AgentId = agentId, ThreadId = threadId,
            EstimatedCostUsd = estimatedCostUsd,
            CachedInputTokens = cachedInputTokens, CacheCreationTokens = cacheCreationTokens
        }, estimatedCostUsd ?? _costCalculator?.CalculateCost(provider, model, inputTokens, outputTokens, cachedInputTokens, cacheCreationTokens), ct);

    public Task LogUsageAsync(UsageLogEntry entry, CancellationToken ct = default)
    {
        Check.NotNull(entry);

        // 调用方给的成本优先，没给按配置的单价算
        var estimatedCostUsd = entry.EstimatedCostUsd
                               ?? _costCalculator?.CalculateCost(entry.Provider, entry.Model, entry.InputTokens,
                                   entry.OutputTokens, entry.CachedInputTokens, entry.CacheCreationTokens);
        return LogUsageCoreAsync(entry, estimatedCostUsd, ct);
    }

    /// <summary>写一行。<paramref name="estimatedCostUsd"/> 已由入口算好（各重载调成本计算器的方式不同）。</summary>
    private async Task LogUsageCoreAsync(UsageLogEntry entry, decimal? estimatedCostUsd, CancellationToken ct)
    {
        var userId = entry.UserId ?? await ResolveUserIdAsync(entry.ThreadId, ct);

        UsageLog? log = null;
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            log = new UsageLog
            {
                OperationType = entry.OperationType,
                Provider = entry.Provider,
                Model = entry.Model,
                InputTokens = entry.InputTokens,
                OutputTokens = entry.OutputTokens,
                TotalTokens = entry.InputTokens + entry.OutputTokens,
                DurationMs = entry.DurationMs,
                IsSuccess = entry.IsSuccess,
                ErrorMessage = entry.ErrorMessage,
                UserId = userId,
                AgentId = entry.AgentId,
                ThreadId = entry.ThreadId,
                EstimatedCostUsd = estimatedCostUsd,
                CachedInputTokens = entry.CachedInputTokens,
                CacheCreationTokens = entry.CacheCreationTokens,
                // 走 GetClientIp：支持反向代理，且受隐私开关
                // AspNetCoreOptions.CollectClientIpAddress 统一约束。
                IpAddress = httpContext?.Request?.GetClientIp(),
                UserAgent = httpContext?.Request?.Headers["User-Agent"].ToString()
            };

            await _repository.InsertAsync(log);

            Logger.LogDebug(
                "Usage logged: {OperationType}, Provider={Provider}, Model={Model}, Tokens={TotalTokens}, Cost={Cost:F6}USD, Duration={Duration}ms, Success={Success}",
                entry.OperationType, entry.Provider, entry.Model, log.TotalTokens, estimatedCostUsd, entry.DurationMs, entry.IsSuccess);
        }
        catch (Exception ex)
        {
            // Logging failure should not affect main flow. 插入失败的那一行仍是 Added，留在变更跟踪器里
            // 会让本作用域下一次 SaveChanges（运行收尾、历史落库）重放它再失败一次，记账失败就成了运行失败。
            if (log != null)
            {
                _repository.Discard(log);
            }

            Logger.LogWarning(ex, "Failed to log usage");
        }
    }

    /// <summary>
    /// 调用方没给用户时这一行计在谁名下：正在执行的运行请求的用户（续跑、后台子运行没有登录用户，只有请求用户），
    /// 其次当前登录用户，再次线程的创建人（没有登录上下文的后台调用只知道线程）。都没有就是不属于任何用户的调用。
    /// </summary>
    /// <remarks>查线程失败只记日志：少了归属的一行仍比丢掉一行账目好。</remarks>
    private async Task<Guid?> ResolveUserIdAsync(Guid? threadId, CancellationToken ct)
    {
        var userId = ServiceProvider.GetService<IAgentExecutionContextAccessor>()?.CurrentRequest?.UserId ?? CurrentUser?.Id;
        if (userId.HasValue || !threadId.HasValue || _threadRepository is null)
        {
            return userId;
        }

        try
        {
            return await _threadRepository.AsNoTracking()
                .Where(t => t.Id == threadId.Value)
                .Select(t => t.CreatorId)
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not resolve the owner of thread {ThreadId} for a usage log row", threadId);
            return null;
        }
    }
}
