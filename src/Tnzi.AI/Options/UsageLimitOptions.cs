namespace Tnzi.AI.Options;

/// <summary>
/// 按用户的用量上限的部署级默认值与窗口设置，绑定 <c>AI:UsageLimits</c>。
/// </summary>
/// <remarks>
/// <para>
/// 计量口径是 <b>API 等价费用</b>（美元）：每一轮按实际消耗的 token（含缓存读写）与各模型的单价折算。外部 CLI 运行用 CLI 自报的成本
/// （claude 报的就是按 API 价目算的），内建路径按 <c>AI:CostTracking</c> 配的单价算，单价没配的模型这一轮记 0 并记一条 Warning。
/// </para>
/// <para>
/// 这里的值是最底下一层：管理员在管理端给租户设的默认值（<c>admin/ai/usage-limits/defaults</c>）与给某个用户设的值逐项覆盖它。
/// 金额为 null = 不限。默认两项都不限：没配单价的部署按金额设上限只会拦不住任何东西，开不开由部署决定。
/// </para>
/// </remarks>
[ConfigSection("AI:UsageLimits")]
public class UsageLimitOptions
{
    /// <summary>每个 5 小时窗口的默认上限（美元）；null = 不限。</summary>
    public decimal? SessionLimitUsd { get; set; }

    /// <summary>每周的默认上限（美元）；null = 不限。</summary>
    public decimal? WeeklyLimitUsd { get; set; }

    /// <summary>「5 小时窗口」的长度。默认 5 小时。</summary>
    public TimeSpan SessionWindow { get; set; } = TimeSpan.FromHours(5);

    /// <summary>「每周窗口」的长度。默认 7 天。</summary>
    public TimeSpan WeeklyWindow { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 接近上限的提示阈值（0-1，默认 0.8）：任一窗口用到这个比例，用户端的用量接口标出 <c>warning</c>，
    /// 发布 <c>UsageLimitThresholdReachedEvent</c>。
    /// </summary>
    public decimal WarningThreshold { get; set; } = 0.8m;

    /// <summary>
    /// 默认允许使用的模型（<c>AI:Chat:Models</c> 里的 id）；空 = 模型清单里的全部。
    /// </summary>
    public List<string> AllowedModels { get; set; } = [];

    /// <summary>默认模型（<c>AI:Chat:Models</c> 里的 id）；null = 允许列表里的第一个。</summary>
    public string? DefaultModel { get; set; }
}

/// <summary>
/// <see cref="UsageLimitOptions"/> 验证器。
/// </summary>
public class UsageLimitOptionsValidator : OptionsValidatorBase<UsageLimitOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(UsageLimitOptions options, List<string> errors)
    {
        if (options.SessionLimitUsd is < 0)
        {
            errors.Add("AI:UsageLimits:SessionLimitUsd must not be negative.");
        }

        if (options.WeeklyLimitUsd is < 0)
        {
            errors.Add("AI:UsageLimits:WeeklyLimitUsd must not be negative.");
        }

        if (options.SessionWindow <= TimeSpan.Zero)
        {
            errors.Add("AI:UsageLimits:SessionWindow must be greater than zero.");
        }

        if (options.WeeklyWindow <= TimeSpan.Zero)
        {
            errors.Add("AI:UsageLimits:WeeklyWindow must be greater than zero.");
        }

        if (options.WarningThreshold is <= 0 or > 1)
        {
            errors.Add("AI:UsageLimits:WarningThreshold must be greater than 0 and at most 1.");
        }

        if (options.AllowedModels?.Any(string.IsNullOrWhiteSpace) == true)
        {
            errors.Add("AI:UsageLimits:AllowedModels must not contain empty model ids.");
        }
    }
}
