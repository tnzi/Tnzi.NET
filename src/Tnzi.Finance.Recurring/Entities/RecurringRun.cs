namespace Tnzi.Finance.Recurring.Entities;

/// <summary>
/// 一次生成的记录
/// </summary>
/// <remarks>
/// ★这张表不是日志，是**幂等键**：<c>(租户, 模板, 期次日期)</c> 唯一索引让同一期
/// 无论被补齐、被手工触发、还是被两个实例同时扫到，都只可能生成一次。没有它，
/// "作业重跑一次"就等于给客户重复开一张发票 —— 而那是要打电话道歉的事故。
///
/// 失败同样落一行（<see cref="Status"/> = Failed）：悄悄跳过的那一期，没有人会发现。
/// 失败行**不占用幂等键**（下次扫描会重试），故唯一索引只覆盖成功与跳过。
/// </remarks>
public class RecurringRun : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属模板</summary>
    public Guid RecurringDocumentId { get; set; }

    /// <summary>期次日期（date-only，即这一期的单据日）</summary>
    public DateTime PeriodDate { get; set; }

    /// <summary>结果</summary>
    public RecurringRunStatus Status { get; set; }

    /// <summary>生成的单据类型（wire 令牌，与总账来源令牌同一套词汇）</summary>
    public string? DocType { get; set; }

    /// <summary>生成的单据 Id</summary>
    public Guid? DocId { get; set; }

    /// <summary>生成的单据编号（过账后才有）</summary>
    public string? DocNumber { get; set; }

    /// <summary>是否已过账</summary>
    public bool Posted { get; set; }

    /// <summary>失败原因（Status=Failed 时）</summary>
    public string? FailReason { get; set; }
}
