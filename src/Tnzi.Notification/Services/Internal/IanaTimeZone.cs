namespace Tnzi.Notification.Services.Internal;

/// <summary>
/// 偏好行上的时区标识：写入时只收 IANA 标识，读取时解析成 <see cref="TimeZoneInfo"/>。读写两头共用这一处。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>只收 IANA（<c>America/Toronto</c>），不收 Windows 标识（<c>Eastern Standard Time</c>）。</b>
/// 浏览器 <c>Intl</c> 给出的、前端能显示的、跨平台都认的都是 IANA；Windows 标识在 Windows 上
/// 也能查到，于是「在开发机上写进去、在 Linux 生产上读」这种分叉会被它掩盖。判据是
/// <see cref="TimeZoneInfo.HasIanaId"/>，不是能不能查到。
/// </para>
/// <para>
/// ★★ <b>只收规范大小写</b>（<c>America/Toronto</c> 收，<c>america/toronto</c> 拒），比对查到的
/// <c>zone.Id</c> 用 ordinal。原因不是挑剔：<see cref="TimeZoneInfo.TryFindSystemTimeZoneById"/>
/// 首次查询区分大小写，而它的进程内缓存不区分，于是同一个小写输入在「本进程里有人先按规范写法
/// 查过」时通过、否则被拒，校验结果取决于查询顺序。逐字比对把它变回确定的答案；
/// 浏览器 <c>Intl</c> 与前端下拉给的本来就是规范写法。
/// </para>
/// </remarks>
internal static class IanaTimeZone
{
    /// <summary>
    /// <see cref="Preference.QuietHoursTimeZone"/> 的列宽。现存最长的 IANA 标识 32 个字符，留足余量。
    /// </summary>
    internal const int MaxLength = 64;

    /// <summary>
    /// 校验调用方给的标识。空白 = 没设（<paramref name="canonical"/> 为 <see langword="null"/>，返回 true）；
    /// 查不到、查到的不是 IANA 标识、或大小写与规范写法不一致时返回 false。
    /// </summary>
    internal static bool TryNormalize(string? id, out string? canonical)
    {
        canonical = null;
        if (string.IsNullOrWhiteSpace(id))
            return true;

        var trimmed = id.Trim();
        if (trimmed.Length > MaxLength
            || !TimeZoneInfo.TryFindSystemTimeZoneById(trimmed, out var zone)
            || !zone.HasIanaId
            || !string.Equals(zone.Id, trimmed, StringComparison.Ordinal))
            return false;

        canonical = trimmed;
        return true;
    }

    /// <summary>
    /// 读取时解析：空白 = UTC；查不到返回 <see langword="null"/>，由调用方决定怎么报。
    /// </summary>
    /// <remarks>
    /// 写入时已经校验过，所以查不到只会是<b>主机的时区数据</b>与写入那台不同
    /// （例如精简容器镜像里没有 tzdata），那是部署问题，调用方必须让它出声。
    /// </remarks>
    internal static TimeZoneInfo? Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Utc;

        return TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone) ? zone : null;
    }
}
