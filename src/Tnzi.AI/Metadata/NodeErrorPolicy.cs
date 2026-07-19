namespace Tnzi.AI.Metadata;

/// <summary>
/// 工作流节点失败时的错误处理策略
/// </summary>
public enum NodeErrorPolicy
{
    /// <summary>
    /// 节点失败时立即中止整个工作流（默认行为）
    /// </summary>
    Fail,

    /// <summary>
    /// 节点失败时跳过该节点，继续执行后续就绪节点（节点输出为空字符串）
    /// </summary>
    Skip,

    /// <summary>
    /// 节点失败时以错误文本作为输出继续执行，不中止工作流
    /// </summary>
    Continue
}
