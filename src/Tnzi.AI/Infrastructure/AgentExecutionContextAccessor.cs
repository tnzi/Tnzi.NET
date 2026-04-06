namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 当前 AI 执行请求访问器。
/// </summary>
public interface IAgentExecutionContextAccessor
{
    AgentRunRequest? CurrentRequest { get; set; }

    /// <summary>
    /// 当前执行周期的共享属性包 — 工具与中间件间传递数据（例如 ClarificationRequest、Todos）
    /// </summary>
    Dictionary<string, object> Properties { get; }

    /// <summary>
    /// 捕获当前属性快照，用于嵌套运行结束后恢复父级上下文。
    /// </summary>
    Dictionary<string, object>? CaptureProperties();

    /// <summary>
    /// 恢复先前捕获的属性快照。
    /// </summary>
    void RestoreProperties(Dictionary<string, object>? properties);

    /// <summary>
    /// 清空当前执行周期属性。
    /// </summary>
    void ClearProperties();
}

/// <summary>
/// 基于 AsyncLocal 的当前 AI 执行请求访问器。
/// </summary>
public sealed class AgentExecutionContextAccessor : IAgentExecutionContextAccessor
{
    private static readonly AsyncLocal<AgentRunRequest?> CurrentRequestHolder = new();
    private static readonly AsyncLocal<Dictionary<string, object>?> PropertiesHolder = new();

    public AgentRunRequest? CurrentRequest
    {
        get => CurrentRequestHolder.Value;
        set => CurrentRequestHolder.Value = value;
    }

    public Dictionary<string, object> Properties
    {
        get => PropertiesHolder.Value ??= [];
    }

    public Dictionary<string, object>? CaptureProperties()
    {
        var properties = PropertiesHolder.Value;
        return properties == null ? null : new Dictionary<string, object>(properties);
    }

    public void RestoreProperties(Dictionary<string, object>? properties)
    {
        PropertiesHolder.Value = properties == null ? null : new Dictionary<string, object>(properties);
    }

    public void ClearProperties()
    {
        PropertiesHolder.Value = null;
    }
}
