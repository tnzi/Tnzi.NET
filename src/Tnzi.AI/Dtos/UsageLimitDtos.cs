namespace Tnzi.AI.Dtos;

/// <summary>
/// 一项可覆盖的上限：不设（跟随上一层）、不限，或一个金额。
/// </summary>
public class UsageLimitSettingDto
{
    /// <summary>取值方式。</summary>
    public UsageLimitMode Mode { get; set; }

    /// <summary>金额（美元）；只在 <see cref="Mode"/> 为 <c>Limited</c> 时有意义，其余取值时忽略。</summary>
    [Range(0, 1_000_000)]
    public decimal? AmountUsd { get; set; }
}

/// <summary>
/// 管理员保存的一份策略（租户的默认值，或某个用户的单独设置）。每一项都可以「不设」，不设的跟随上一层。
/// </summary>
public class UsagePolicyDto
{
    /// <summary>5 小时上限。</summary>
    public UsageLimitSettingDto SessionLimit { get; set; } = new();

    /// <summary>每周上限。</summary>
    public UsageLimitSettingDto WeeklyLimit { get; set; } = new();

    /// <summary>允许使用的模型 id；null = 跟随上一层。</summary>
    public List<string>? AllowedModels { get; set; }

    /// <summary>默认模型 id；null = 跟随上一层。</summary>
    public string? DefaultModel { get; set; }

    /// <summary>备注，只给管理员看。</summary>
    public string? Note { get; set; }

    /// <summary>最后修改时间；从没保存过时为 null。</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 保存一份策略的请求。
/// </summary>
public class SaveUsagePolicyDto
{
    /// <summary>5 小时上限。</summary>
    [Required]
    public UsageLimitSettingDto SessionLimit { get; set; } = null!;

    /// <summary>每周上限。</summary>
    [Required]
    public UsageLimitSettingDto WeeklyLimit { get; set; } = null!;

    /// <summary>允许使用的模型 id（取自模型清单）；null = 跟随上一层。给出时不能为空列表。</summary>
    public List<string>? AllowedModels { get; set; }

    /// <summary>默认模型 id；null = 跟随上一层。必须在生效的允许列表里。</summary>
    [MaxLength(200)]
    public string? DefaultModel { get; set; }

    /// <summary>备注，只给管理员看。</summary>
    [MaxLength(512)]
    public string? Note { get; set; }
}

/// <summary>
/// 逐项取完三层之后真正生效的策略。
/// </summary>
public class EffectiveUsagePolicyDto
{
    /// <summary>5 小时上限（美元）；null = 不限。</summary>
    public decimal? SessionLimitUsd { get; set; }

    /// <summary>每周上限（美元）；null = 不限。</summary>
    public decimal? WeeklyLimitUsd { get; set; }

    /// <summary>允许使用的模型（模型清单里的项，按清单顺序）；没配模型清单时为空。</summary>
    public List<ChatModelDto> AllowedModels { get; set; } = [];

    /// <summary>默认模型 id；没有可用模型时为 null。</summary>
    public string? DefaultModel { get; set; }
}

/// <summary>
/// 配置文件 <c>AI:UsageLimits</c> 这一层（管理端「不设」最终跟随的值）。
/// </summary>
public class UsageLimitConfigurationDto
{
    /// <summary>5 小时上限（美元）；null = 不限。</summary>
    public decimal? SessionLimitUsd { get; set; }

    /// <summary>每周上限（美元）；null = 不限。</summary>
    public decimal? WeeklyLimitUsd { get; set; }

    /// <summary>允许使用的模型 id；空 = 模型清单里的全部。</summary>
    public List<string> AllowedModels { get; set; } = [];

    /// <summary>默认模型 id。</summary>
    public string? DefaultModel { get; set; }

    /// <summary>5 小时窗口的长度（分钟）。</summary>
    public int SessionWindowMinutes { get; set; }

    /// <summary>每周窗口的长度（分钟）。</summary>
    public int WeeklyWindowMinutes { get; set; }

    /// <summary>接近上限的提示阈值（0-1）。</summary>
    public decimal WarningThreshold { get; set; }
}

/// <summary>
/// 所有人的默认值（管理端「默认值」设置区）。
/// </summary>
public class UsageLimitDefaultsDto
{
    /// <summary>管理员给这个租户保存的默认值；从没保存过时每一项都是「不设」。</summary>
    public UsagePolicyDto Policy { get; set; } = new();

    /// <summary>配置文件那一层：默认值里「不设」的项跟随它。</summary>
    public UsageLimitConfigurationDto Configuration { get; set; } = new();

    /// <summary>生效的默认值。</summary>
    public EffectiveUsagePolicyDto Effective { get; set; } = new();

    /// <summary>模型清单（<c>AI:Chat:Models</c>），选择允许的模型时从这里挑。</summary>
    public List<ChatModelDto> Catalog { get; set; } = [];
}

/// <summary>
/// 某个用户的单独设置、生效值与当前用量（管理端「用户的上限与模型」区）。
/// </summary>
public class UserUsageLimitsDto
{
    /// <summary>用户 ID。</summary>
    public Guid UserId { get; set; }

    /// <summary>这个用户是否有单独设置。</summary>
    public bool HasCustomPolicy { get; set; }

    /// <summary>这个用户的单独设置；没有时每一项都是「不设」。</summary>
    public UsagePolicyDto Policy { get; set; } = new();

    /// <summary>所有人的默认值（这个用户「不设」的项取的就是它）。</summary>
    public EffectiveUsagePolicyDto Defaults { get; set; } = new();

    /// <summary>这个用户生效的值。</summary>
    public EffectiveUsagePolicyDto Effective { get; set; } = new();

    /// <summary>模型清单。</summary>
    public List<ChatModelDto> Catalog { get; set; } = [];

    /// <summary>当前各窗口的用量。</summary>
    public UsageLimitStatusDto Usage { get; set; } = new();
}

/// <summary>
/// 一个用户当前各窗口的用量（管理端看，带金额）。
/// </summary>
public class UsageLimitStatusDto
{
    /// <summary>两个窗口，5 小时在前。</summary>
    public List<UsageWindowStatusDto> Windows { get; set; } = [];

    /// <summary>接近上限的提示阈值（0-1）。</summary>
    public decimal WarningThreshold { get; set; }

    /// <summary>这份数据是什么时候读的（UTC）。</summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>最后一次被计量的时间（UTC）；从没用过时为 null。</summary>
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>
/// 一个窗口的用量（管理端看，带金额）。
/// </summary>
public class UsageWindowStatusDto
{
    /// <summary>哪个窗口。</summary>
    public UsageLimitWindow Window { get; set; }

    /// <summary>窗口长度（分钟）。</summary>
    public int DurationMinutes { get; set; }

    /// <summary>上限（美元）；null = 不限。</summary>
    public decimal? LimitUsd { get; set; }

    /// <summary>这个窗口里已经用掉的费用（美元）。</summary>
    public decimal UsedUsd { get; set; }

    /// <summary>已用百分比（0-100，跑完一轮超出时会大于 100）；不限时为 null。</summary>
    public decimal? UsedPercent { get; set; }

    /// <summary>窗口开始时间（UTC）；还没开始时为 null。</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>窗口重置时间（UTC）；还没开始时为 null。</summary>
    public DateTime? ResetsAt { get; set; }

    /// <summary>已经到上限（下一轮会被拦下）。</summary>
    public bool Exceeded { get; set; }

    /// <summary>已经过了接近上限的提示阈值。</summary>
    public bool WarningReached { get; set; }
}

/// <summary>
/// 当前用户自己的用量（用户端，只有百分比，不给金额）。
/// </summary>
public class MyUsageLimitsDto
{
    /// <summary>两个窗口，5 小时在前。</summary>
    public List<MyUsageWindowDto> Windows { get; set; } = [];

    /// <summary>接近上限的提示阈值（0-1）。</summary>
    public decimal WarningThreshold { get; set; }

    /// <summary>这份数据是什么时候读的（UTC），界面上的「上次更新」。</summary>
    public DateTime CheckedAt { get; set; }
}

/// <summary>
/// 当前用户一个窗口的用量。
/// </summary>
public class MyUsageWindowDto
{
    /// <summary>哪个窗口。</summary>
    public UsageLimitWindow Window { get; set; }

    /// <summary>这个窗口有没有上限。没有上限时不给百分比。</summary>
    public bool Limited { get; set; }

    /// <summary>窗口长度（分钟，来自 <c>AI:UsageLimits</c>），界面据此写「5 小时」而不是写死。</summary>
    public int DurationMinutes { get; set; }

    /// <summary>已用百分比（0-100，可能略超 100）；不限时为 null。</summary>
    public decimal? UsedPercent { get; set; }

    /// <summary>窗口开始时间（UTC）；还没开始时为 null。</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>窗口重置时间（UTC）；还没开始时为 null（5 小时窗口从下一次使用起算）。</summary>
    public DateTime? ResetsAt { get; set; }

    /// <summary>已经到上限：发消息会被拦下，直到 <see cref="ResetsAt"/>。</summary>
    public bool Exceeded { get; set; }

    /// <summary>已经过了接近上限的提示阈值。</summary>
    public bool WarningReached { get; set; }
}

/// <summary>
/// 一轮因为用量上限被拦下时随错误一起返回的详情（<c>errorDetails</c>）。
/// </summary>
public class UsageLimitDenialDto
{
    /// <summary>哪个窗口到上限了（两个都到时报更晚重置的那个：要等到它才能再用）。</summary>
    public UsageLimitWindow Window { get; set; }

    /// <summary>什么时候重置（UTC）；上限设为 0 时没有重置时间，为 null。</summary>
    public DateTime? ResetsAt { get; set; }
}

/// <summary>
/// 管理端计量列表的查询参数。
/// </summary>
public class UsageMeterQueryDto : PagedQueryDto
{
    /// <summary>只看这个用户（可选）。</summary>
    public Guid? UserId { get; set; }
}

/// <summary>
/// 管理端计量列表的一行：一个用过 AI 的用户和他当前的用量。
/// </summary>
public class UserUsageSummaryDto
{
    /// <summary>用户 ID。</summary>
    public Guid UserId { get; set; }

    /// <summary>这个用户是否有单独设置。</summary>
    public bool HasCustomPolicy { get; set; }

    /// <summary>当前各窗口的用量。</summary>
    public List<UsageWindowStatusDto> Windows { get; set; } = [];

    /// <summary>最后一次被计量的时间（UTC）。</summary>
    public DateTime? LastUsedAt { get; set; }
}
