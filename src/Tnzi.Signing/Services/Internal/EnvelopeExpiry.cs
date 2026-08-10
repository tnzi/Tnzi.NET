namespace Tnzi.Signing.Services.Internal;

/// <summary>
/// 请求过期的判定。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EnvelopeStatus.Expired"/> <b>不落库</b>，一律按 <c>ExpiresAt</c> 现算。
/// 原因是过期不是任何人做了什么的结果，而是时间到了 —— 要把它写进库就得有个东西定时来扫，
/// 而在那个东西跑起来之前的那段时间里，库里的状态是错的。现算则任何时刻都对。
/// </para>
/// <para>
/// ★ 筛选谓词（<see cref="StatusFilter"/>）与展示派生（<see cref="Derive"/>）<b>必须同源</b>：
/// 两处各写一遍的话，「按已过期筛选」选出来的行和列表里标着「已过期」的行迟早对不上。
/// 不变式：<c>StatusFilter(s)</c> 选中的每一行，经 <see cref="Derive"/> 后都必须得到 <c>s</c>
/// （由 <c>EnvelopeExpiryTests</c> 对全部 7 个状态逐一钉住）。
/// </para>
/// </remarks>
internal static class EnvelopeExpiry
{
    /// <summary>
    /// 还在等人签的状态 —— 只有这些会过期。
    /// </summary>
    /// <remarks>
    /// 已完成 / 已拒签 / 已作废都是终态：那份文件的结局已经定了，之后再过多久都不会变成"过期"。
    /// </remarks>
    public static bool IsPending(EnvelopeStatus status)
        => status is EnvelopeStatus.Sent or EnvelopeStatus.InProgress;

    /// <summary>把库里的状态换算成"此刻真实的状态"。</summary>
    public static EnvelopeStatus Derive(EnvelopeStatus stored, DateTime expiresAt, DateTime now)
        => IsPending(stored) && expiresAt <= now ? EnvelopeStatus.Expired : stored;

    /// <summary>
    /// 按<b>派生后</b>的状态筛选（可直接翻成 SQL）。
    /// </summary>
    public static Expression<Func<Envelope, bool>> StatusFilter(EnvelopeStatus status, DateTime now)
    {
        if (status == EnvelopeStatus.Expired)
        {
            // 第一项不是多余的：本模块自己从不写 Expired，但它是公开枚举成员 ——
            // 消费应用或将来的扫描任务真把它写进库时，这里认不出来就会让那一行
            // 在列表里标着"已过期"却怎么筛都筛不到。
            return e => e.Status == EnvelopeStatus.Expired
                        || ((e.Status == EnvelopeStatus.Sent || e.Status == EnvelopeStatus.InProgress)
                            && e.ExpiresAt <= now);
        }

        // 等待中的状态要排掉那些"其实已经过期了"的行，否则它们会同时出现在
        // Sent 和 Expired 两个筛选结果里。
        if (IsPending(status))
            return e => e.Status == status && e.ExpiresAt > now;

        return e => e.Status == status;
    }
}
