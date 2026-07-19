namespace Tnzi.Audit.Metadata;

/// <summary>
/// 实体状态（对应EF Core的EntityState）
/// </summary>
public enum EntityState
{
    /// <summary>
    /// 未变更
    /// </summary>
    Unchanged = 0,

    /// <summary>
    /// 已添加
    /// </summary>
    Added = 1,

    /// <summary>
    /// 已修改
    /// </summary>
    Modified = 2,

    /// <summary>
    /// 已删除
    /// </summary>
    Deleted = 3,

    /// <summary>
    /// 已分离
    /// </summary>
    Detached = 4
}
