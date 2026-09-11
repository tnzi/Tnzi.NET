namespace Tnzi.Identity.Extensions;

/// <summary>
/// <see cref="PendingUserActions"/> 的判定辅助。
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <strong>只能在内存里用，不能进 EF 查询。</strong>这些是普通扩展方法，
/// LINQ-to-Entities 翻译不了它们（<c>Enum.HasFlag</c> 同样翻译不了）。
/// 数据库侧的筛选必须把按位与写进表达式本身，并且把标志值提成局部变量：
/// </para>
/// <code>
/// var flag = query.PendingAction.Value;
/// queryable = queryable.Where(u => (u.PendingActions &amp; flag) != PendingUserActions.None);
/// </code>
/// <para>
/// 写成扩展方法而不是让每处各写一遍按位表达式，是因为
/// <c>(x &amp; Flag) != None</c> 与 <c>(x &amp; Flag) == Flag</c> 在多位标志上语义不同，
/// 而调用点关心的从来都是「有没有欠这件事」这一个意思。
/// </para>
/// </remarks>
public static class PendingUserActionsExtensions
{
    /// <summary>
    /// 这个账号是否欠着 <paramref name="action"/> 里的<b>任意一件</b>事。
    /// </summary>
    public static bool HasPendingAction(this User user, PendingUserActions action)
    {
        Check.NotNull(user);

        return (user.PendingActions & action) != PendingUserActions.None;
    }

    /// <summary>
    /// 取出这个账号欠着的<b>义务</b>（放行但必须先办完的那些），没有则为
    /// <see cref="PendingUserActions.None"/>。
    /// </summary>
    public static PendingUserActions GetOwedObligations(this User user)
    {
        Check.NotNull(user);

        return user.PendingActions & PendingUserActions.Obligations;
    }

    /// <summary>
    /// 把标志集合拆成单个位的名字，供 API 返回给前端。
    /// </summary>
    /// <remarks>
    /// 刻意返回名字而不是数值：前端据此渲染「还差哪几步」，而数值要求两侧对同一套位
    /// 保持一致 —— 位是会新增的，名字则是自解释的。
    /// </remarks>
    public static IReadOnlyList<string> ToActionNames(this PendingUserActions actions)
    {
        if (actions == PendingUserActions.None)
        {
            return [];
        }

        // 只列单位标志，跳过 None 与 Blocking / Obligations 这两个聚合值 ——
        // 它们是分类用的，不是「一件要办的事」。
        return Enum.GetValues<PendingUserActions>()
            .Where(v => v != PendingUserActions.None
                && v != PendingUserActions.Blocking
                && v != PendingUserActions.Obligations
                && IsSingleBit(v)
                && (actions & v) != PendingUserActions.None)
            .Select(v => v.ToString())
            .ToList();
    }

    private static bool IsSingleBit(PendingUserActions value)
    {
        var bits = (int)value;
        return bits != 0 && (bits & (bits - 1)) == 0;
    }
}
