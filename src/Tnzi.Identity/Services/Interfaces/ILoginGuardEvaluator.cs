namespace Tnzi.Identity.Services;

/// <summary>
/// 登录守卫求值器：收集容器里注册的全部 <see cref="ILoginGuard"/>，按 <see cref="ILoginGuard.Order"/>
/// 升序执行，首个拒绝即短路。框架在每条令牌签发路径上调用它。
/// </summary>
/// <remarks>
/// 始终注册（即使没有任何守卫），这样签发路径无需判空；无守卫时直接放行，零开销。
/// </remarks>
public interface ILoginGuardEvaluator
{
    /// <summary>
    /// 是否存在已注册的守卫。用于在调用方省掉构造上下文的开销。
    /// </summary>
    bool HasGuards { get; }

    /// <summary>
    /// 依次执行守卫。全部放行返回 <see cref="LoginGuardResult.Allow"/>；
    /// 任一拒绝立即返回该守卫的结果（后续守卫不再执行）。
    /// </summary>
    /// <param name="context">求值上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<LoginGuardResult> EvaluateAsync(LoginGuardContext context, CancellationToken cancellationToken = default);
}
