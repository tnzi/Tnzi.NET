
namespace Tnzi.AI.Services;

/// <summary>
/// Usage log service implementation.
/// </summary>
public class UsageLogService : ApplicationService, IUsageLogService
{
    private readonly IRepository<UsageLog, Guid> _repository;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UsageLogService(
        IRepository<UsageLog, Guid> repository,
        IHttpContextAccessor httpContextAccessor,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _httpContextAccessor = Check.NotNull(httpContextAccessor);
    }

    public async Task LogUsageAsync(
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
                IpAddress = httpContext?.Connection?.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request?.Headers["User-Agent"].ToString()
            };

            await _repository.InsertAsync(log);

            Logger.LogDebug(
                "Usage logged: {OperationType}, Provider={Provider}, Model={Model}, Tokens={TotalTokens}, Duration={Duration}ms, Success={Success}",
                operationType, provider, model, log.TotalTokens, durationMs, isSuccess);
        }
        catch (Exception ex)
        {
            // Logging failure should not affect main flow
            Logger.LogWarning(ex, "Failed to log usage");
        }
    }
}