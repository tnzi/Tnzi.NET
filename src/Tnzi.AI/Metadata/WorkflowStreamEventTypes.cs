namespace Tnzi.AI.Metadata;

/// <summary>
/// 工作流流式事件类型常量
/// </summary>
public static class WorkflowStreamEventTypes
{
    /// <summary>The run has started; carries the execution id and nothing else. Always the first event.</summary>
    public const string Started = "started";
    /// <summary>One step finished (or was skipped); sent when it happens, in completion order.</summary>
    public const string Step = "step";
    public const string Completed = "completed";
    public const string Error = "error";
}
