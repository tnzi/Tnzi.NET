namespace Tnzi.AI.Services;

/// <summary>
/// <see cref="IThreadOriginPolicy"/> 的默认实现：页面声明的来源必须在 <c>AI:Chat:AllowedThreadOrigins</c> 里（序数比较），
/// 没有声明就没有来源；自己不给值。
/// </summary>
public class ConfiguredThreadOriginPolicy : IThreadOriginPolicy
{
    private readonly IOptionsMonitor<AiChatOptions> _options;

    /// <summary>初始化。</summary>
    public ConfiguredThreadOriginPolicy(IOptionsMonitor<AiChatOptions> options)
    {
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public Task<Result<string?>> ResolveAsync(ThreadOriginRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (request.DeclaredOrigin is not { } origin)
        {
            return Task.FromResult(Result<string?>.Success(null));
        }

        var allowed = _options.CurrentValue.AllowedThreadOrigins;
        return Task.FromResult(allowed is { Count: > 0 } && allowed.Contains(origin, StringComparer.Ordinal)
            ? Result<string?>.Success(origin)
            : NotAllowed(origin));
    }

    /// <summary>页面声明了一个不被允许的来源时的拒绝（400 <c>AI_THREAD_ORIGIN_NOT_ALLOWED</c>）。自己实现策略的应用可以沿用它。</summary>
    public static Result<string?> NotAllowed(string origin)
        => Result<string?>.Failure($"Thread origin '{origin}' cannot be declared by a client.", 400, ErrorCodes.ThreadOriginNotAllowed);
}
