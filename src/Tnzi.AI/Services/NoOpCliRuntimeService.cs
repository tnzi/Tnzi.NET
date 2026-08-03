namespace Tnzi.AI.Services;

/// <summary>
/// 未加载 <c>Tnzi.AI.Cli</c> 时的运行时注册表回退：一律 501 并给出接入指引。
/// </summary>
public class NoOpCliRuntimeService : ICliRuntimeService, INoOpService
{
    private const string Message =
        "External CLI agent runtimes require the Tnzi.AI.Cli module. "
        + "Add [DependsOn(typeof(AICliModule))] and set AI:Cli:Enabled=true to enable it.";

    /// <inheritdoc />
    public Task<Result<List<CliRuntimeDto>>> GetListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<List<CliRuntimeDto>>(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public Task<Result<CliRuntimeDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<CliRuntimeDto>(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public Task<Result<CliRuntimeProbeResultDto>> ProbeAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<CliRuntimeProbeResultDto>(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public Task<Result<CliRuntimeDto>> UpdateAsync(
        Guid id, UpdateCliRuntimeDto input, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<CliRuntimeDto>(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure(Message, 501, ErrorCodes.CliModuleNotLoaded));

    /// <inheritdoc />
    public Task<Result<List<CliProviderOptionDto>>> GetProviderOptionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<List<CliProviderOptionDto>>(Message, 501, ErrorCodes.CliModuleNotLoaded));
}
