namespace Tnzi.AI.Cli.Engine;

/// <summary>
/// CLI Agent 执行请求
/// </summary>
public class CliAgentRequest
{
    /// <summary>用户提示（通过 stdin 传入 CLI）</summary>
    public required string Prompt { get; init; }

    /// <summary>工作目录（必需）</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>模型名称（可选）</summary>
    public string? Model { get; init; }

    /// <summary>会话 ID（用于续接，仅支持原生会话的 CLI 可用）</summary>
    public string? SessionId { get; init; }

    /// <summary>允许的工具列表（可选）</summary>
    public IReadOnlyList<string>? AllowedTools { get; init; }

    /// <summary>额外环境变量</summary>
    public Dictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>超时秒数</summary>
    public int TimeoutSeconds { get; init; } = 600;
}

/// <summary>
/// CLI 统一中间事件
/// </summary>
public class CliAgentEvent
{
    /// <summary>事件类型</summary>
    public required CliEventType EventType { get; init; }

    /// <summary>内容文本</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>工具名称（仅 ToolUse/ToolResult）</summary>
    public string? ToolName { get; init; }

    /// <summary>工具调用 ID（仅 ToolUse/ToolResult）</summary>
    public string? ToolId { get; init; }

    /// <summary>会话 ID（仅 Status 事件的 init 子类型）</summary>
    public string? SessionId { get; init; }

    /// <summary>是否为错误</summary>
    public bool IsError { get; init; }

    /// <summary>元数据（cost、duration、turns 等非 token 信息）</summary>
    public Dictionary<string, JsonElement>? Metadata { get; init; }

    /// <summary>归一化 Token 用量（仅 Complete 事件填充，由 provider 从 CLI 输出解析）</summary>
    public TokenUsageDto? Usage { get; init; }
}

/// <summary>
/// CLI 事件类型
/// </summary>
public enum CliEventType
{
    /// <summary>助手回复文本</summary>
    Text,

    /// <summary>思考过程</summary>
    Thinking,

    /// <summary>工具调用</summary>
    ToolUse,

    /// <summary>工具执行结果</summary>
    ToolResult,

    /// <summary>状态信息（初始化、重试等）</summary>
    Status,

    /// <summary>错误信息</summary>
    Error,

    /// <summary>执行完成（包含 cost/usage 元数据）</summary>
    Complete
}
