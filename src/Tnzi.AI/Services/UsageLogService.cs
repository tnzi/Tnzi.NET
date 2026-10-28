
namespace Tnzi.AI.Services;

/// <summary>
/// Usage log service implementation.
/// </summary>
public class UsageLogService : ApplicationService, IUsageLogService
{
    private readonly IRepository<UsageLog, Guid> _repository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICostCalculator? _costCalculator;

    public UsageLogService(
        IRepository<UsageLog, Guid> repository,
        IHttpContextAccessor httpContextAccessor,
        IServiceProvider serviceProvider,
        ICostCalculator? costCalculator = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _httpContextAccessor = Check.NotNull(httpContextAccessor);
        _costCalculator = costCalculator;
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
    {
        // 自动计算成本
        var cost = _costCalculator?.CalculateCost(provider, model, inputTokens, outputTokens);
        return LogUsageCoreAsync(operationType, provider, model, inputTokens, outputTokens, durationMs, isSuccess, errorMessage, agentId, threadId, cost, 0, 0, ct);
    }

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
    {
        // 使用外部传入的成本，若为 null 则自动计算
        var cost = estimatedCostUsd ?? _costCalculator?.CalculateCost(provider, model, inputTokens, outputTokens);
        return LogUsageCoreAsync(operationType, provider, model, inputTokens, outputTokens, durationMs, isSuccess, errorMessage, agentId, threadId, cost, 0, 0, ct);
    }

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
    {
        var cost = _costCalculator?.CalculateCost(provider, model, inputTokens, outputTokens, cachedInputTokens, cacheCreationTokens);
        return LogUsageCoreAsync(operationType, provider, model, inputTokens, outputTokens, durationMs, isSuccess, errorMessage, agentId, threadId, cost, cachedInputTokens, cacheCreationTokens, ct);
    }

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
    {
        var cost = estimatedCostUsd
                   ?? _costCalculator?.CalculateCost(provider, model, inputTokens, outputTokens, cachedInputTokens, cacheCreationTokens);
        return LogUsageCoreAsync(operationType, provider, model, inputTokens, outputTokens, durationMs, isSuccess, errorMessage, agentId, threadId, cost, cachedInputTokens, cacheCreationTokens, ct);
    }

    private async Task LogUsageCoreAsync(
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
        CancellationToken ct)
    {
        UsageLog? log = null;
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            log = new UsageLog
            {
                OperationType = operationType,
                Provider = provider,
                Model = model,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                DurationMs = durationMs,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage,
                AgentId = agentId,
                ThreadId = threadId,
                EstimatedCostUsd = estimatedCostUsd,
                CachedInputTokens = cachedInputTokens,
                CacheCreationTokens = cacheCreationTokens,
                // 走 GetClientIp：支持反向代理，且受隐私开关
                // AspNetCoreOptions.CollectClientIpAddress 统一约束。
                IpAddress = httpContext?.Request?.GetClientIp(),
                UserAgent = httpContext?.Request?.Headers["User-Agent"].ToString()
            };

            await _repository.InsertAsync(log);

            Logger.LogDebug(
                "Usage logged: {OperationType}, Provider={Provider}, Model={Model}, Tokens={TotalTokens}, Cost={Cost:F6}USD, Duration={Duration}ms, Success={Success}",
                operationType, provider, model, log.TotalTokens, estimatedCostUsd, durationMs, isSuccess);
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
}