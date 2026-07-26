namespace Tnzi.Security.Claims;

/// <summary>
/// 把用户 Id 解析成可显示的名字（**可选契约**）。
/// </summary>
/// <remarks>
/// 谁写的这条评论、谁挂的这个附件——凡是要把 <c>CreatorId</c> 显示给人看的地方
/// 都需要它，而这些地方大多在**不引用 Identity 的模块**里（Finance 就刻意零
/// Identity 引用）。让每个这样的模块各自去引 Identity，等于把可选模块变成必选。
///
/// 由 Identity 模块提供实现；未加载时**根本不注册**，消费方按可选依赖注入
/// （<c>IUserDisplayNameProvider? provider = null</c>）并在缺失时把名字留空——
/// 呈现端回落到"某人"。可选契约缺失只会**少给信息**，不会多给。
///
/// 批量而非逐个：一条讨论线上十条评论若逐个解析，就是十次往返。
/// </remarks>
[StableApi(Since = "0.1.0")]
public interface IUserDisplayNameProvider
{
    /// <summary>
    /// 批量解析显示名；解析不到的 Id **不出现在结果里**（调用方按缺失处理，
    /// 不必区分"查不到"与"空名字"）。
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}
