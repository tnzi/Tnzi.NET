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
}
