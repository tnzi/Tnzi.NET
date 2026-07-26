namespace Tnzi.Finance.Entities;

/// <summary>
/// 账本封账锁（每租户单行）：设定"账已封到某日"，该日及之前禁止过账与冲销。
/// </summary>
/// <remarks>
/// <b>与会计年度关账的区别</b>：<see cref="FiscalYear"/> 锁的是**整个年度区间**，而封账日是一个
/// **滚动的日期** —— 记账员每月对完账就往前推一格（QuickBooks 的 closing date）。用会计年度
/// 表达不出"封到上月末"，除非去造假的年度。两把锁并存、语义正交：年度锁按区间，封账日按截止点。
/// <br/><br/>
/// <b>为什么是实体而不是配置</b>：封账进度是**每租户的账本状态**（多租户下各租户进度不同），
/// 且必须留痕（谁在什么时候把它推到哪一天）。`FinanceOptions` 是每部署的启动/运行配置，
/// 承载不了这两点。
/// <br/><br/>
/// <b>口令不是安全机制</b>：它是**摩擦装置** —— 让越过封账线变成一个刻意动作，配合
/// <c>LastModifierId</c> 与审计模块就有了归属。真正的访问控制仍是 <c>finance.ledgerLock.update</c>
/// 权限码。
/// </remarks>
public class LedgerLock : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 单行判别列：恒为 <see cref="SingletonScope"/>。
    /// </summary>
    /// <remarks>
    /// 每租户单行这件事需要一个可建唯一索引的锚点。用固定判别列而不是"服务层查完再插"，
    /// 是因为后者是 check-then-act，并发下会插出两行封账日（然后校验读到哪一行全看运气）。
    /// </remarks>
    public string Scope { get; set; } = SingletonScope;

    /// <summary>单行判别值。</summary>
    public const string SingletonScope = "ledger";

    /// <summary>
    /// 封账日（含当日）。<c>null</c> = 未封账。
    /// </summary>
    /// <remarks>该日**及之前**的过账/冲销一律拒绝，与已关闭会计年度同为 409。</remarks>
    public DateTime? ClosingDate { get; set; }

    /// <summary>
    /// 修改封账日所需口令的哈希。<c>null</c> = 不需要口令（仅凭权限码即可改）。
    /// </summary>
    /// <remarks>只存哈希，明文永不落库、永不出现在任何 DTO 上。</remarks>
    public string? PasswordHash { get; set; }

    /// <summary>最近一次变更的说明（"已报 Q2 GST/HST"），进审计留痕。</summary>
    public string? Note { get; set; }
}
