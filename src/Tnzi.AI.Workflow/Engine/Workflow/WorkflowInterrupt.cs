namespace Tnzi.AI.Workflow.Engine;

/// <summary>
/// 通用工作流中断描述 - 任何节点均可通过 CheckInterruptAsync 返回此对象暂停工作流
/// </summary>
/// <remarks>
/// 灵感来源于 LangGraph 的 interrupt()/Command(resume=) 模式。
/// 支持审批、人工输入、外部事件回调三种中断类型。
/// </remarks>
[ExperimentalApi(Reason = "Generic workflow interrupt is in preview")]
public class WorkflowInterrupt
{
    /// <summary>中断发生的步骤 ID</summary>
    public string StepId { get; init; } = null!;

    /// <summary>中断原因（供人类审阅的描述）</summary>
    public string Reason { get; init; } = null!;

    /// <summary>中断类型</summary>
    public InterruptType Type { get; init; }

    /// <summary>
    /// 请求的输入字段定义（Key: 字段名, Value: 字段描述/Schema 信息）
    /// 用于 HumanInput 类型，告知前端需要收集哪些数据
    /// </summary>
    public Dictionary<string, object>? RequestedInput { get; init; }

    /// <summary>
    /// 中断超时时间（超时后可自动失败或跳过）
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// 工作流中断类型
/// </summary>
public enum InterruptType
{
    /// <summary>审批（需要人工批准/拒绝）</summary>
    Approval = 0,

    /// <summary>人工输入（需要人工提供数据）</summary>
    HumanInput = 1,

    /// <summary>外部事件（等待外部系统回调）</summary>
    ExternalEvent = 2
}
