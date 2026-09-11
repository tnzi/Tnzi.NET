namespace Tnzi.Identity.Services;

/// <summary>
/// 接受邀请时，**由消费应用决定**要收集什么、要求走完哪些步骤。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <strong>这是整套邀请机制里唯一的业务接入点，也是框架刻意止步的那条线。</strong>
/// 「新人要填哪些字段」「要不要强制绑 TOTP」「不同角色要求是否不同」这些东西的形态
/// 高度依赖具体应用：有的公司只要一个密码，有的要求管理员必须先绑 TOTP，
/// 有的要收工号、部门、紧急联系人。框架把这些做进去，只会得到一个每个消费方都要绕开的半成品。
/// </para>
/// <para>
/// ★★ <strong>框架守的是另一件事，而且只守这一件：</strong>
/// <see cref="InvitationAcceptOutcome.Completed"/> 为 <c>false</c> 时，
/// 账号<b>绝不</b>从 <see cref="PendingUserActions.InvitationPending"/> 转出。
/// 这条边界必须在服务端 —— 把「必须绑 TOTP」放在前端判断，等于没做：
/// 接受端点是匿名可达的，跳过一次前端校验就绕过去了。
/// </para>
/// <para>
/// ★ <strong>可以被调用多次。</strong>需要分步完成时（先设密码、再扫码绑 TOTP），
/// 返回 <c>Completed = false</c> 加上 <see cref="InvitationAcceptOutcome.RemainingSteps"/>，
/// 令牌<b>不会</b>被消费，用户可以拿同一条链接回来继续。
/// 这与 passkey 注册令牌把校验和消费分成两步是同一个理由：
/// 用户在系统弹窗上点了取消，不该白白烧掉一枚令牌。
/// </para>
/// <para>
/// 框架提供 <see cref="DefaultInvitationAcceptanceHandler"/>（设密码 + 补基础资料）
/// 让简单场景开箱即用；消费应用注册自己的实现即可整体替换，无需继承。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "载荷形态仍可能补充结构化的步骤描述")]
public interface IInvitationAcceptanceHandler
{
    /// <summary>
    /// 消化被邀请人提交的内容，并回答「这张邀请可以算完成了吗」。
    /// </summary>
    /// <param name="user">被邀请的账号，此刻仍是 <see cref="PendingUserActions.InvitationPending"/>。</param>
    /// <param name="input">本次提交（密码与消费应用自定义载荷）。</param>
    /// <param name="profile">管理员发邀请时先行填好的资料，可为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 返回失败（<c>Result</c> 不成功）表示这次提交不合法（密码太弱、必填项缺失），
    /// 消息会原样回到前端；此时令牌同样不被消费。
    /// </remarks>
    Task<Result<InvitationAcceptOutcome>> AcceptAsync(
        User user,
        AcceptInvitationDto input,
        JsonElement? profile,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 接受邀请这一步的处理结果。
/// </summary>
/// <param name="Completed">
/// 是否可以激活账号了。<b>只有 true 才会让框架解除 Pending 状态并消费令牌。</b>
/// </param>
/// <param name="RemainingSteps">
/// 还差哪些步骤，原样交给前端。字符串的含义由消费应用自己定义
/// （框架不认识 <c>"EnrollTotp"</c> 是什么，只负责传递）。
/// </param>
public sealed record InvitationAcceptOutcome(
    bool Completed,
    IReadOnlyList<string>? RemainingSteps = null)
{
    /// <summary>全部完成，可以激活。</summary>
    public static InvitationAcceptOutcome Done() => new(true);

    /// <summary>还差几步，账号保持未激活。</summary>
    public static InvitationAcceptOutcome NeedsMore(params string[] steps) => new(false, steps);
}
