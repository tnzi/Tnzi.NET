namespace Tnzi.Notification.Metadata;

/// <summary>
/// 网关回执对一份传真的结论。
/// </summary>
/// <remarks>
/// ★ <b><see cref="Unknown"/> 是一等结论，不是"解析失败"的占位</b>：email-to-fax 网关的回执
/// 没有任何标准可言，每一家的措辞都不一样。把认不出来的回执归成"失败"，等于凭一句读不懂的话
/// 去推翻一次已经成功的投递；归成"成功"则更糟。所以三态里只有 <see cref="Failed"/> 会改动数据，
/// 另外两个只留日志。
/// </remarks>
public enum FaxDeliveryOutcome
{
    /// <summary>
    /// 读不出结论：不是回执、措辞不认识，或者同时出现成功与失败的字样。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 网关报告已送达。
    /// </summary>
    Delivered = 1,

    /// <summary>
    /// 网关报告投递失败（占线、无应答、号码无效、页数超限等）。
    /// </summary>
    Failed = 2
}
