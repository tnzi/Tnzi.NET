namespace Tnzi.AI.Metadata;

/// <summary>
/// 按用户的用量上限的两个窗口。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Session"/>：没有进行中的窗口时，第一次使用开一个，持续 <c>AI:UsageLimits:SessionWindow</c>（默认 5 小时），到点重置；
/// 之后的第一次使用再开下一个。与 claude.ai 的「当前会话」同一个语义。
/// </para>
/// <para>
/// <see cref="Weekly"/>：从这个用户第一次被计量的时刻起，每 <c>AI:UsageLimits:WeeklyWindow</c>（默认 7 天）一个周期，首尾相接，
/// 所以重置总在一周里的同一天、同一时刻（「周四 10:00 重置」）。
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<UsageLimitWindow>))]
public enum UsageLimitWindow
{
    /// <summary>5 小时窗口（从窗口内第一次使用起算）。</summary>
    Session = 0,

    /// <summary>每周窗口（周期首尾相接）。</summary>
    Weekly = 1
}

/// <summary>
/// 一项可覆盖的上限设置的取值方式。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<UsageLimitMode>))]
public enum UsageLimitMode
{
    /// <summary>不设，跟随上一层（用户 → 租户默认 → 配置文件）。</summary>
    Inherit = 0,

    /// <summary>不限。</summary>
    Unlimited = 1,

    /// <summary>限定为一个金额（美元）。</summary>
    Limited = 2
}
