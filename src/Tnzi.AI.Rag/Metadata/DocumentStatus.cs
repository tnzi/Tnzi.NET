namespace Tnzi.AI.Rag.Metadata;

/// <summary>
/// 文档处理状态
/// </summary>
public enum DocumentStatus
{
    /// <summary>
    /// 处理中
    /// </summary>
    Processing = 0,

    /// <summary>
    /// 处理完成
    /// </summary>
    Completed = 1,

    /// <summary>
    /// 处理失败
    /// </summary>
    Failed = 2
}
