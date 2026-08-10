namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// 保留策略的被试实体。
/// </summary>
/// <remarks>
/// 继承 <see cref="FullAuditedEntity{TKey}"/> 是刻意的：它带 <c>ISoftDelete</c>，
/// 而框架仓储对软删实体执行的是「把 IsDeleted 置真」。销毁如果退化成软删，
/// 数据仍在库里也仍在备份里——这是这套机制最容易做错的地方，
/// 所以被试实体必须是软删实体，否则测试会在最重要的那条上给出假绿。
/// </remarks>
public class RetentionTestRecord : FullAuditedEntity<Guid>
{
    /// <summary>业务分类，用于验证策略的 <c>Scope</c> 条件。</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>业务意义上的结案时间，用于验证「按哪个时间字段判定到期」。</summary>
    public DateTime ClosedAt { get; set; }
}
