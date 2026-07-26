namespace Tnzi.Finance.Recurring.Entities;

/// <summary>
/// 周期性单据模板：一条排期 + 一份单据的内容
/// </summary>
/// <remarks>
/// 覆盖的是"每月 1 号给这个客户开同一张发票"这类工作 —— 订阅、租金、维保、固定
/// 服务费。它**本身不是单据**：不投影总账、没有编号、没有金额被承认；到期时它
/// 按内容**造出一张真单据**，那张才是事实。
///
/// ★两条与直觉相反但必要的设计：
/// <list type="number">
/// <item><b>默认生成草稿</b>（<see cref="AutoPost"/> 为 null 时跟随
///   <c>Finance:Recurring:DefaultAutoPost</c>，出厂 false）。让日历直接往总账里
///   写东西，是最容易到月底才被发现的那种错。</item>
/// <item><b>补齐语义交给消费方</b>（<c>CatchUpPolicy</c>）。作业停了一周，该补出
///   七张日租发票还是只补最近一张，不同生意里都是对的，框架不替他们猜。</item>
/// </list>
/// </remarks>
public class RecurringDocument : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>模板名称（给人看的，例如 "Acme - monthly retainer"）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>生成哪种单据</summary>
    public RecurringDocKind Kind { get; set; }

    /// <summary>状态</summary>
    public RecurringStatus Status { get; set; } = RecurringStatus.Active;

    /// <summary>往来方（Invoice=客户；Bill/Expense=供应商）</summary>
    public Guid PartyId { get; set; }

    /// <summary>费用的付款科目（仅 <see cref="RecurringDocKind.Expense"/> 用）</summary>
    public Guid? PaidFromAccountId { get; set; }

    /// <summary>交易币种（null = 本位币）</summary>
    public string? Currency { get; set; }

    /// <summary>结算方式（费用与账单可选）</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>写进生成单据的摘要</summary>
    public string? Memo { get; set; }

    // ── 排期 ──────────────────────────────────────────────

    /// <summary>周期</summary>
    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;

    /// <summary>每几个周期一次（1 = 每期，3 = 每三期）</summary>
    public int Interval { get; set; } = 1;

    /// <summary>
    /// 锚点：月度/季度/年度取几号（1-31），周度取星期几（1=周一 … 7=周日）。
    /// </summary>
    /// <remarks>
    /// null = 跟随 <see cref="StartDate"/> 那天。31 号落在只有 30 天的月份时**收到
    /// 月末**而不是溢出到下月 1 号 —— "每月最后一天开票"是真实存在的约定，而多开
    /// 一天等于把账期悄悄错开。
    /// </remarks>
    public int? AnchorDay { get; set; }

    /// <summary>首次生成日（date-only）</summary>
    public DateTime StartDate { get; set; }

    /// <summary>结束日（date-only，含当天）；null = 无限期</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>最多生成几次；null = 不限</summary>
    public int? MaxOccurrences { get; set; }

    /// <summary>到期天数（生成时 DueDate = 单据日 + 本值）；null = 跟随往来方账期</summary>
    public int? DueDays { get; set; }

    /// <summary>
    /// 生成后是否直接过账；null = 跟随 <c>Finance:Recurring:DefaultAutoPost</c>。
    /// </summary>
    /// <remarks>
    /// 三态而非布尔：模板级"就跟全局走"与"这条我明确要求不过账"是两件事，压成布尔
    /// 会让改全局默认时悄悄改掉一批人明确表过态的模板。
    /// </remarks>
    public bool? AutoPost { get; set; }

    // ── 运行状态 ──────────────────────────────────────────

    /// <summary>下一次应生成的日期（date-only）</summary>
    public DateTime NextRunDate { get; set; }

    /// <summary>最近一次生成的日期</summary>
    public DateTime? LastRunDate { get; set; }

    /// <summary>已生成次数</summary>
    public int OccurrenceCount { get; set; }

    /// <summary>行</summary>
    public virtual ICollection<RecurringLine> Lines { get; set; } = [];
}
