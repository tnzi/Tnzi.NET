namespace Tnzi.AI.Options;

/// <summary>
/// 配额默认值配置选项
/// </summary>
public class QuotaOptions
{
    /// <summary>
    /// 新用户默认每日 Token 限额（默认 100 万）
    /// </summary>
    public long DefaultDailyTokenLimit { get; set; } = 1_000_000;

    /// <summary>
    /// 新用户默认每月 Token 限额（默认 2000 万）
    /// </summary>
    public long DefaultMonthlyTokenLimit { get; set; } = 20_000_000;
}
