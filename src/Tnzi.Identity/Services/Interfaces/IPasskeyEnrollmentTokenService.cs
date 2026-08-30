namespace Tnzi.Identity.Services;

/// <summary>
/// Passkey 注册令牌：一次性、绑定目标身份、可配有效期、消费即失效。
/// </summary>
/// <remarks>
/// <para>
/// <strong>这是原语，不是邀请流程。</strong>它只回答一个问题：
/// 「这个还没有任何凭据的人，凭什么可以往<em>这个</em>账号上挂一枚 passkey」。
/// </para>
/// <para>
/// ★ <strong>刻意不做的：</strong>谁有资格邀请谁、要不要审批、发邮件还是发短信、
/// 邀请落地后给什么角色、能不能转让、批量导入 —— <strong>全部是业务流程，形态高度依赖应用</strong>。
/// 框架把它们做进来，只会得到一个每个消费方都要绕开的半成品。
/// 因此本服务<strong>不暴露任何端点</strong>：签发是业务决策，由应用自己的控制器调 <see cref="IssueAsync"/>
/// 并决定怎么把令牌递到用户手上。
/// </para>
/// <para>
/// ★ <strong>校验与消费是分开的两步。</strong>`Validate` 不消费，`Consume` 才失效。
/// 因为 WebAuthn 是两段式的：begin 时要能校验、complete 成功时才该作废。
/// 合成一步会让「用户在系统弹窗上点了取消」白白烧掉一枚令牌。
/// </para>
/// <para>
/// <strong>存储：</strong>复用 <c>AuthToken</c> 实体（<c>LoginProvider = "Passkey"</c>、
/// <c>Name = "EnrollmentToken"</c>），零迁移，且自动继承既有的过期清理后台任务与
/// <c>[AuditIgnore]</c> 豁免。★ <strong>只存哈希不存原文</strong>，且刻意不加盐不慢哈希 ——
/// 令牌是 256 位随机数，没有字典可查，而按哈希等值查询要求确定性，加盐就查不了了
/// （与 <c>Tnzi.Signing</c> 的签署令牌同一判据）。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "注册令牌的载荷仍可能补充用途标记或签发者信息")]
public interface IPasskeyEnrollmentTokenService
{
    /// <summary>
    /// 为指定用户签发一枚注册令牌，<strong>返回原文（此后不再可读）</strong>。
    /// </summary>
    /// <param name="userId">令牌指向的用户。</param>
    /// <param name="lifetime">有效期；不给则用 <c>Identity:Passkey:EnrollmentTokenLifetimeMinutes</c>。</param>
    /// <remarks>
    /// 同一用户同时只保留一枚有效令牌：重新签发会覆盖旧的（唯一索引使然，也是想要的语义 ——
    /// 补发一张就该让上一张作废）。
    /// </remarks>
    Task<Result<PasskeyEnrollmentTokenDto>> IssueAsync(Guid userId, TimeSpan? lifetime = null);

    /// <summary>
    /// 校验令牌，有效则返回它指向的用户 ID。<strong>不消费</strong>。
    /// </summary>
    /// <remarks>失效、过期、不存在一律返回同一个 400，不区分 —— 区分开就是在帮人试探。</remarks>
    Task<Result<Guid>> ValidateAsync(string token);

    /// <summary>
    /// 消费令牌使其立即失效。重复消费是幂等的（第二次同样返回成功）。
    /// </summary>
    Task<Result> ConsumeAsync(string token);
}
