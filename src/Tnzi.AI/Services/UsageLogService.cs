
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
        var cost = _costCalculator?.CalculateCost(provider, model, inputTokens, outputTokens);
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
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var log = new UsageLog
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
                IpAddress = httpContext?.Connection?.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request?.Headers["User-Agent"].ToString()
            };

            await _repository.InsertAsync(log);

            Logger.LogDebug(
                "Usage logged: {OperationType}, Provider={Provider}, Model={Model}, Tokens={TotalTokens}, Cost={Cost:F6}USD, Duration={Duration}ms, Success={Success}",
                operationType, provider, model, log.TotalTokens, estimatedCostUsd, durationMs, isSuccess);
        }
        catch (Exception ex)
        {
            // Logging failure should not affect main flow
            Logger.LogWarning(ex, "Failed to log usage");
        }
    }
}