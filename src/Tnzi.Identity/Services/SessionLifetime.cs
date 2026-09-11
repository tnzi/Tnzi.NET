namespace Tnzi.Identity.Services;

/// <summary>
/// 会话生命周期的两条算术：绝对上限怎么算、滑动续期怎么被它夹住。
/// </summary>
/// <remarks>
/// 单独抽出来是因为它必须在<b>两个</b>会话实现（数据库 / Redis）里给出同一个答案。
/// 三行代码抄两遍不会当场出错，但「其中一个实现忘了夹绝对上限」这种偏差
/// 不会有任何症状 —— 只是那个部署上的会话又变回可以无限续期，而配置项照常显示已生效。
/// </remarks>
public static class SessionLifetime
{
    /// <summary>
    /// 按配置算出会话的绝对过期时间；<paramref name="absoluteLifetimeHours"/> 小于等于 0 时返回 <c>null</c>（不设上限）。
    /// </summary>
    public static DateTime? ComputeAbsoluteExpiry(DateTime createdAt, int absoluteLifetimeHours)
        => absoluteLifetimeHours > 0 ? createdAt.AddHours(absoluteLifetimeHours) : null;

    /// <summary>
    /// 一条会话此刻还活着吗。四个判据合在一处。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 抽成纯函数的两个理由：
    /// ① 数据库与 Redis 两个实现必须给出<b>同一个</b>答案，各抄一遍不会当场出错，
    ///    但「其中一个忘了判闲置超时」没有任何症状 —— 只是那个部署上的会话永远不闲置到期，
    ///    而配置项照常显示已生效；
    /// ② 闲置超时默认关闭（<c>SessionTimeoutMinutes = 0</c>），集成测试跑的恒是「不启用」那条路径，
    ///    判据留在私有方法里就<b>没有任何测试能证明它启用时真的工作</b>。
    /// </para>
    /// <para>
    /// 四条里任意一条成立即判死，取最先到的那个。
    /// </para>
    /// </remarks>
    /// <param name="isRevoked">会话是否已被撤销</param>
    /// <param name="expiresAt">滑动硬过期（随刷新令牌续期）；<c>null</c> 表示不过期（遗留会话）</param>
    /// <param name="absoluteExpiresAt">绝对上限（建立时定死，续期不推动）；<c>null</c> 表示不设上限</param>
    /// <param name="lastActivityTime">最后一次请求的时刻</param>
    /// <param name="idleTimeoutMinutes">闲置超时（分钟）；<c>0</c> 或负数表示不启用</param>
    /// <param name="now">当前时刻（UTC）</param>
    public static bool IsAlive(
        bool isRevoked,
        DateTime? expiresAt,
        DateTime? absoluteExpiresAt,
        DateTime lastActivityTime,
        int idleTimeoutMinutes,
        DateTime now)
    {
        if (isRevoked)
        {
            return false;
        }

        if (expiresAt.HasValue && expiresAt.Value <= now)
        {
            return false;
        }

        // 绝对上限：与活跃程度无关，到点就得重新认证（ASVS 7.3.2）。
        if (absoluteExpiresAt.HasValue && absoluteExpiresAt.Value <= now)
        {
            return false;
        }

        // 闲置超时（ASVS 7.3.1）。
        if (idleTimeoutMinutes > 0 && lastActivityTime.AddMinutes(idleTimeoutMinutes) <= now)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 后台维护「撤销长期失活会话」用的阈值：闲置超时与刷新令牌周期里更短的那个。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 这两个数落在<b>同一个字段</b>（<see cref="Entities.UserSession.LastActivityTime"/>）上，
    /// 却曾经各说各话：闲置超时把会话<b>算</b>死在 N 分钟，而后台任务按刷新令牌周期（默认 7 天）
    /// 才把它<b>写</b>成已撤销。中间那段时间里，一条早就用不了的会话在管理端列表、
    /// 会话统计、个人中心的「当前活跃设备」里仍显示为活跃 —— 那几处查询只看 <c>IsRevoked</c>。
    /// 取更短的那个，让库里的记录追上判定。
    /// </para>
    /// <para>
    /// ★ <b>闲置超时为 0（默认，不启用）时必须不参与比较</b>：直接 <c>Min</c> 会得到 0，
    /// 那意味着「所有会话都算失活」—— 后台任务下一轮就把全站用户踢下线。
    /// </para>
    /// </remarks>
    /// <param name="idleTimeoutMinutes">闲置超时（分钟）；<c>0</c> 或负数表示不启用</param>
    /// <param name="refreshTokenExpirationDays">刷新令牌生命周期（天）；下限按 1 天处理</param>
    public static TimeSpan ResolveInactiveThreshold(int idleTimeoutMinutes, int refreshTokenExpirationDays)
    {
        var refreshWindow = TimeSpan.FromDays(Math.Max(1, refreshTokenExpirationDays));
        if (idleTimeoutMinutes <= 0)
        {
            return refreshWindow;
        }

        var idleWindow = TimeSpan.FromMinutes(idleTimeoutMinutes);
        return idleWindow < refreshWindow ? idleWindow : refreshWindow;
    }

    /// <summary>
    /// 把一次滑动续期夹在绝对上限之内。
    /// </summary>
    /// <param name="requestedExpiresAt">调用方希望续到的时间（一般是「现在 + 刷新令牌周期」）</param>
    /// <param name="absoluteExpiresAt">会话的绝对上限；<c>null</c> 表示不设上限，原样返回</param>
    public static DateTime ClampToAbsolute(DateTime requestedExpiresAt, DateTime? absoluteExpiresAt)
        => absoluteExpiresAt.HasValue && requestedExpiresAt > absoluteExpiresAt.Value
            ? absoluteExpiresAt.Value
            : requestedExpiresAt;
}
