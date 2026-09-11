namespace Tnzi.Identity.Tests;

/// <summary>
/// <see cref="PendingUserActions"/> 的分类完整性。
/// </summary>
/// <remarks>
/// ★★★ <strong>这条门禁是把两件事塞进一个标志字段之所以成立的前提。</strong>
/// 字段里一半的位阻断登录（守卫读）、一半只是义务（签发流程读），
/// 而「新增一个位」这件事在类型层面看不出它属于哪一半。少了这条断言，
/// 加一个位却忘了归类的后果是 <b>它静默地什么都不做</b> ——
/// 守卫不看它、签发流程也不看它，而写它的那段代码一切正常、测试全绿。
/// </remarks>
public class PendingUserActionsTests
{
    [Fact]
    public void EveryDefinedAction_IsClassifiedAsBlockingOrObligation()
    {
        var unclassified = SingleBitActions()
            .Where(v => (v & (PendingUserActions.Blocking | PendingUserActions.Obligations)) == PendingUserActions.None)
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            $"These actions belong to neither Blocking nor Obligations: {string.Join(", ", unclassified)}. "
            + "An unclassified flag is read by nobody: the login guard ignores it and so does the token-issuing path, "
            + "so whatever sets it appears to work while changing nothing.");
    }


    /// <summary>
    /// ★★★ 每一个义务位都必须在 <see cref="IPendingActionService"/> 上有一条完成路径。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="PendingUserActions"/> 是密封枚举，消费应用**加不了新位**，只能用框架定义的这几个。
    /// 所以「定义一个位却不给完成路径」不是留白，是陷阱：应用置上它之后，
    /// 用户会收到挑战、却永远办不完，账号从此进不去 —— 而框架这一侧一切正常，
    /// 没有任何测试会红。
    /// </para>
    /// <para>
    /// 判据按方法名匹配（<c>Complete{位名}Async</c>）。这要求命名保持规律，
    /// 而那正是想要的：加一个义务位就得加一个同名的完成方法。
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryObligation_HasACompletionPathOnTheService()
    {
        var methods = typeof(IPendingActionService)
            .GetMethods()
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = SingleBitActions()
            .Where(a => (a & PendingUserActions.Obligations) != PendingUserActions.None)
            .Where(a => !methods.Contains($"Complete{a}Async"))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"These obligations have no completion path on IPendingActionService: {string.Join(", ", missing)}. "
            + "A consumer that sets one of them leaves the user permanently challenged and permanently locked out, "
            + $"with nothing on the framework side looking wrong. Add Complete<Action>Async for each.");
    }

    /// <summary>
    /// 两组必须互斥。一个位同时是「拒绝登录」和「登录后再办」是自相矛盾的，
    /// 而它的实际表现取决于哪段代码先跑，那种缺陷极难定位。
    /// </summary>
    [Fact]
    public void BlockingAndObligations_DoNotOverlap()
    {
        Assert.Equal(
            PendingUserActions.None,
            PendingUserActions.Blocking & PendingUserActions.Obligations);
    }

    /// <summary>
    /// 阻断位必须落在低位段、义务位落在高位段。
    /// </summary>
    /// <remarks>
    /// 位置本身是分类的记录：下一个加位的人得先决定它属于哪一半才知道该写哪个数字。
    /// 允许两段交错，那个决定就没有任何东西提醒他去做。
    /// </remarks>
    [Fact]
    public void BlockingBitsAreLow_AndObligationBitsAreHigh()
    {
        const int boundary = 1 << 8;

        foreach (var action in SingleBitActions())
        {
            var isBlocking = (action & PendingUserActions.Blocking) != PendingUserActions.None;
            if (isBlocking)
            {
                Assert.True((int)action < boundary, $"{action} is blocking but sits in the obligation range.");
            }
            else
            {
                Assert.True((int)action >= boundary, $"{action} is an obligation but sits in the blocking range.");
            }
        }
    }

    /// <summary>
    /// 位值必须留在 JS 的按位运算安全区内 —— 前端也读这个字段。
    /// </summary>
    /// <remarks>
    /// JS 的位运算按 32 位有符号数做，<c>1 &lt;&lt; 31</c> 会变成负数。
    /// C# 侧不会有任何症状，前端的 <c>(actions &amp; flag) !== 0</c> 却会给出错误答案。
    /// </remarks>
    [Fact]
    public void AllBits_StayWithinJavaScriptSafeRange()
    {
        foreach (var action in SingleBitActions())
        {
            Assert.True((int)action > 0 && (int)action < 1 << 30, $"{action} is outside the range JavaScript can mask safely.");
        }
    }

    /// <summary>
    /// 清一位不该动到其它位。这条钉住的是「用 <c>&amp;= ~Flag</c> 而不是赋 <c>None</c>」这个写法。
    /// </summary>
    [Fact]
    public void ClearingOneAction_LeavesTheOthersOwed()
    {
        var owed = PendingUserActions.InvitationPending | PendingUserActions.ChangePassword;

        var afterAccepting = owed & ~PendingUserActions.InvitationPending;

        Assert.Equal(PendingUserActions.ChangePassword, afterAccepting);
    }

    /// <summary>
    /// 单个位的名字要能拆出来给前端，而聚合值（None / Blocking / Obligations）不该混进去。
    /// </summary>
    [Fact]
    public void ToActionNames_ListsSingleBitsOnly()
    {
        var names = (PendingUserActions.ChangePassword | PendingUserActions.EnrollTotp).ToActionNames();

        Assert.Equal(2, names.Count);
        Assert.Contains(nameof(PendingUserActions.ChangePassword), names);
        Assert.Contains(nameof(PendingUserActions.EnrollTotp), names);
        Assert.DoesNotContain(nameof(PendingUserActions.Obligations), names);
        Assert.Empty(PendingUserActions.None.ToActionNames());
    }

    /// <summary>
    /// 枚举里定义的、恰好只占一位的成员（跳过 None 与两个聚合值）。
    /// </summary>
    private static IEnumerable<PendingUserActions> SingleBitActions()
        => Enum.GetValues<PendingUserActions>()
            .Where(v => v != PendingUserActions.None
                && v != PendingUserActions.Blocking
                && v != PendingUserActions.Obligations)
            .Where(v => { var b = (int)v; return b != 0 && (b & (b - 1)) == 0; })
            .Distinct();
}
