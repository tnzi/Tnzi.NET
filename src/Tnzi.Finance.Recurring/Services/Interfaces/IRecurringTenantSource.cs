namespace Tnzi.Finance.Recurring.Services.Interfaces;

/// <summary>
/// 后台扫描要覆盖哪些租户（可选契约）
/// </summary>
/// <remarks>
/// 后台作业没有请求，也就没有租户上下文，而模板表是分租户的。开了多租户的部署
/// 必须告诉框架"有哪些租户要扫" —— 框架不认识租户目录（Finance 刻意不引用租户
/// 模块），只能问。
///
/// ★**未注册时只扫环境上下文（单租户即全部）**，而不是扫全表：缺省方向必须是
/// "少生成"。凭空给一个不该收到账单的租户开出发票，比漏跑一期严重得多；漏跑看
/// 得见（模板停在过期的 NextRunDate 上），多开只有对方会告诉你。
/// </remarks>
public interface IRecurringTenantSource
{
    /// <summary>要扫描的租户 Id 列表</summary>
    Task<IReadOnlyList<Guid>> GetTenantIdsAsync(CancellationToken cancellationToken = default);
}
