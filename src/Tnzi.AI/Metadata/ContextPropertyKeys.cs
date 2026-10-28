namespace Tnzi.AI.Metadata;

/// <summary>
/// 工具→中间件属性包键名常量
/// </summary>
public static class ContextPropertyKeys
{
    public const string ClarificationRequest = "ClarificationRequest";
    public const string Todos = "Todos";
    public const string IsSubAgent = "IsSubAgent";
    public const string SubAgentName = "SubAgentName";
    public const string CurrentRunId = "CurrentRunId";
    public const string WorkflowExecutionId = "WorkflowExecutionId";
    public const string WorkflowNodeName = "WorkflowNodeName";
    public const string ParentSessionRules = "ParentSessionRules";

    /// <summary>
    /// 本次运行里 <c>save_memory</c> 成功写入过记忆（值为 <c>true</c>）。自动沉淀据此跳过这一轮，
    /// 不再把模型刚亲手存下的内容重复提炼一遍。
    /// </summary>
    public const string MemorySavedThisRun = "MemorySavedThisRun";
}
