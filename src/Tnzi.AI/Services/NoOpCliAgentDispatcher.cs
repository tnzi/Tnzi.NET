namespace Tnzi.AI.Services;

/// <summary>
/// 未加载 <c>Tnzi.AI.Cli</c> 时的外部 agent 调度回退：一律 501 并给出接入指引。
/// </summary>
public class NoOpCliAgentDispatcher : ICliAgentDispatcher, INoOpService
{
    private const string Message =
        "External CLI agent execution requires the Tnzi.AI.Cli module. "
        + "Add [DependsOn(typeof(AICliModule))] and set AI:Cli:Enabled=true to enable it.";

    /// <inheritdoc />
    public Task<Result<Guid>> EnqueueAsync(CliRunRequestDto request, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<Guid>(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public async IAsyncEnumerable<CliAgentEvent> StreamAsync(
        Guid runId, int fromSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new CliAgentEvent { Type = CliAgentEventType.Error, Content = Message };
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Result<CliRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<CliRunDto>(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public Task<Result<IPagedList<CliRunDto>>> GetListAsync(
        CliRunQueryDto query, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<IPagedList<CliRunDto>>(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public Task<Result<List<CliRunMessageDto>>> GetMessagesAsync(
        Guid runId, int fromSequence = 0, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<List<CliRunMessageDto>>(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public Task<Result> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public Task<Result> CancelAsSystemAsync(Guid runId, string? reason = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public Task<Result<int>> CancelThreadRunsAsSystemAsync(Guid threadId, string? reason = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<int>(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    /// <remarks>没有外部执行就没有外部运行：答空列表，而不是「说不出来」。</remarks>
    public Task<IReadOnlyList<Guid>?> GetActiveThreadRunIdsAsSystemAsync(Guid threadId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Guid>?>([]);

    /// <inheritdoc />
    /// <remarks>没有外部执行就没有外部运行：每个 id 都是不存在的运行。</remarks>
    public Task<IReadOnlyDictionary<Guid, CliRunStatus>?> GetStatusesAsSystemAsync(
        IReadOnlyCollection<Guid> runIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, CliRunStatus>?>(new Dictionary<Guid, CliRunStatus>());
}
