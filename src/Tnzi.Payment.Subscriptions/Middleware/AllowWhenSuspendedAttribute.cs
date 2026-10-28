namespace Tnzi.Payment.Subscriptions.Middleware;

/// <summary>
/// 计费账户暂停服务时这个端点照常可用（访问门禁放行）。标在控制器或 action 上。
/// </summary>
/// <remarks>
/// 给消费方自己的、停服期间也必须能用的端点（例如「联系我们」、机构资料页）：比在 <c>AllowedPaths</c> 里写路径更稳，
/// 路由改了它跟着走。框架自己的放行端点走内置路径清单（见 <see cref="SubscriptionAccessGateOptions.BuiltInAllowedPaths"/>），
/// 因为它们所在的程序集不引用本模块。
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AllowWhenSuspendedAttribute : Attribute
{
}
