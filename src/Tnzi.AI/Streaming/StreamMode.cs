namespace Tnzi.AI.Streaming;

/// <summary>
/// 流式输出粒度模式（支持组合使用）
/// </summary>
[Flags]
public enum StreamMode
{
    /// <summary>仅消息文本流（默认）</summary>
    Messages = 1,

    /// <summary>包含步骤事件（Agent 启动/完成、Handoff、Workflow 步骤等）</summary>
    Steps = 2,

    /// <summary>包含调试事件（中间件进出、上下文注入、Token 预算等）</summary>
    Debug = 4,

    /// <summary>包含后续建议（在最终 chunk 中发送）</summary>
    Suggestion = 8
}
