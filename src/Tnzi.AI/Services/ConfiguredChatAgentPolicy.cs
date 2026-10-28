namespace Tnzi.AI.Services;

/// <summary>
/// <see cref="IChatAgentPolicy"/> 的默认实现：按 <c>AI:Chat</c> 配置判定。
/// </summary>
public class ConfiguredChatAgentPolicy : IChatAgentPolicy
{
    private readonly IOptionsMonitor<AiChatOptions> _options;

    /// <summary>初始化。</summary>
    public ConfiguredChatAgentPolicy(IOptionsMonitor<AiChatOptions> options)
    {
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public Task<Result> CheckAsync(Guid? agentId, Guid? userId, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;

        if (agentId is not { } id || id == Guid.Empty)
        {
            return Task.FromResult(options.AllowAgentlessChat
                ? Result.Success()
                : Result.Failure("Chat requires an agent in this deployment.", 403, ErrorCodes.ChatAgentNotAllowed));
        }

        var allowed = options.AllowedAgentIds;
        return Task.FromResult(allowed is not { Count: > 0 } || allowed.Contains(id)
            ? Result.Success()
            : Result.Failure("This agent is not available for chat.", 403, ErrorCodes.ChatAgentNotAllowed));
    }
}
