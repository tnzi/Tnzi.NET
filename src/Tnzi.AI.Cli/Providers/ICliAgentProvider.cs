namespace Tnzi.AI.Cli.Providers;

/// <summary>
/// CLI Agent 提供者标准接口。
/// 每种 CLI 工具（Claude Code, Codex, Gemini CLI 等）实现此接口。
/// </summary>
public interface ICliAgentProvider
{
    /// <summary>提供者名称，与配置中的 key 对应（如 "claude-code"）</summary>
    string ProviderName { get; }

    /// <summary>是否支持原生会话续接（如 Claude Code 的 --resume）</summary>
    bool SupportsSession { get; }

    /// <summary>
    /// 构建子进程启动参数。Prompt 通过 stdin 传入，不作为 CLI 参数。
    /// </summary>
    ProcessStartInfo BuildProcess(CliAgentRequest request, CliProviderOptions providerOptions);

    /// <summary>从 stdout 解析为统一事件流</summary>
    IAsyncEnumerable<CliAgentEvent> ParseOutputAsync(StreamReader stdout, CancellationToken ct);

    /// <summary>将 CLI 事件映射为 AgentStreamChunk</summary>
    AgentStreamChunk? MapToStreamChunk(CliAgentEvent evt);
}
