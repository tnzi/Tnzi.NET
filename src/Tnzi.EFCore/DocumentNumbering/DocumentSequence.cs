
namespace Tnzi.EFCore.DocumentNumbering;

/// <summary>
/// 单据连续编号序列（按租户 + 作用域）
/// </summary>
/// <remarks>
/// 与 Snowflake 流水号不同，本序列提供无缺口的连续编号（多数辖区对
/// 会计凭证/发票编号有连续性法定要求）。分配必须在调用方的工作单元事务内进行：
/// UPDATE 行锁将并发分配串行化，事务回滚时号码随之回收，保证无缺口。
/// 框架级通用原语——发票号/支票号等任意法定连续号消费方均可复用（表名无前缀 <c>DocumentSequence</c>）。
/// </remarks>
public class DocumentSequence : EntityBase<Guid>, IMultiTenant
{
    /// <summary>
    /// 序列作用域（如 "JournalEntry"、"Invoice"，消费方自定义）
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// 下一个待分配值
    /// </summary>
    public long NextValue { get; set; } = 1;

    /// <summary>
    /// 租户ID
    /// </summary>
    public Guid? TenantId { get; set; }
}
