namespace Tnzi.Security.Claims;

/// <summary>
/// 把用户 Id 解析成他的账号邮箱（**可选契约**）。
/// </summary>
/// <remarks>
/// <para>
/// 令牌里不一定带邮箱声明，所以 <see cref="ICurrentUser.Email"/> 可能是空的；而后台任务（续费扣款、催款、
/// 自动开票）连当前用户都没有，手里只有一个用户 Id。要把收据或催款信寄给「开通这条订阅的那个人」，
/// 就得按 Id 查账号上的邮箱，而做这件事的模块（Payment 等）刻意不引用 Identity。
/// </para>
/// <para>
/// 由 Identity 模块提供实现；未加载时**根本不注册**，消费方按可选依赖注入
/// （<c>IUserEmailProvider? provider = null</c>）并在缺失时按「没有地址」处理。可选契约缺失只会少给信息。
/// 批量而非逐个，与 <see cref="IUserDisplayNameProvider"/> 同形。
/// </para>
/// </remarks>
[StableApi(Since = "0.1.0")]
public interface IUserEmailProvider
{
    /// <summary>
    /// 批量解析账号邮箱；查不到或没有邮箱的 Id **不出现在结果里**。
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetEmailsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}
