namespace Tnzi.AI.Entities;

/// <summary>
/// 管理员设的 AI 用量与模型策略：一行管一个用户，或者（<see cref="UserId"/> 为 null）管这个租户的所有人。
/// </summary>
/// <remarks>
/// <para>
/// 三层取值，每一项各自往下找：用户这一行 → 租户默认这一行 → 配置文件 <c>AI:UsageLimits</c>。
/// 一项是「不设」（<see cref="UsageLimitMode.Inherit"/>，模型列表与默认模型为 null）时跟随下一层；
/// 没有这一行等于每一项都不设。
/// </para>
/// <para>
/// 计量的状态（窗口从什么时候开始、用了多少）不在这里，在 <see cref="AiUsageMeter"/>：策略是管理员偶尔改的，
/// 计量每一轮都写，放在一行里，管理员保存策略时的整行回写会盖掉同一时刻落下的用量。
/// </para>
/// </remarks>
public class AiUsagePolicy : AuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户 ID。</summary>
    public Guid? TenantId { get; set; }

    /// <summary>这一行管哪个用户；null = 这个租户所有人的默认值。</summary>
    public Guid? UserId { get; set; }

    /// <summary>5 小时上限的取值方式。</summary>
    public UsageLimitMode SessionLimitMode { get; set; }

    /// <summary>5 小时上限（美元）；只在 <see cref="SessionLimitMode"/> 为 <see cref="UsageLimitMode.Limited"/> 时有值。</summary>
    public decimal? SessionLimitUsd { get; set; }

    /// <summary>每周上限的取值方式。</summary>
    public UsageLimitMode WeeklyLimitMode { get; set; }

    /// <summary>每周上限（美元）；只在 <see cref="WeeklyLimitMode"/> 为 <see cref="UsageLimitMode.Limited"/> 时有值。</summary>
    public decimal? WeeklyLimitUsd { get; set; }

    /// <summary>
    /// 允许使用的模型 id（JSON 数组，取自 <c>AI:Chat:Models</c>）；null = 跟随下一层。
    /// </summary>
    public string? AllowedModelsJson { get; set; }

    /// <summary>默认模型 id；null = 跟随下一层。</summary>
    public string? DefaultModel { get; set; }

    /// <summary>备注（为什么这样设），只给管理员看。</summary>
    public string? Note { get; set; }
}
