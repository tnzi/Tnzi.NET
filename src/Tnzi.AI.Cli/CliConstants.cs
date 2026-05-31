namespace Tnzi.AI.Cli;

/// <summary>
/// CLI 模块共享常量
/// </summary>
public static class CliConstants
{
    /// <summary>标记 CLI Provider 支持原生会话，HistoryMiddleware 据此决定是否跳过</summary>
    public const string SupportsSessionKey = "Cli.SupportsSession";

    /// <summary>AgentRunRequest.Metadata 中的工作目录 key</summary>
    public const string WorkingDirectoryKey = "workingDirectory";

    /// <summary>AiMiddlewareContext.Properties 中的 CLI 会话 ID key</summary>
    public const string CliSessionIdKey = "CliSessionId";
}
