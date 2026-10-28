namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// Ajax限制特性，限制操作只能通过 Ajax 请求访问（请求须带 <c>X-Requested-With: XMLHttpRequest</c> 头，否则答 400）
/// </summary>
/// <remarks>
/// 经 <see cref="ServiceFilterAttribute"/> 把 <see cref="AjaxOnlyFilter"/> 带进标了它的 action 的过滤器管线，
/// 与 <see cref="UnitOfWorkAttribute"/> 同一种接法。它曾是一个裸 <c>Attribute</c>：过滤器不是全局过滤器，
/// 也没有任何东西读这个特性，标了它的端点照样接受跨站表单提交与普通导航。
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AjaxOnlyAttribute : ServiceFilterAttribute
{
    /// <summary>
    /// 初始化一个<see cref="AjaxOnlyAttribute"/>类型的新实例
    /// </summary>
    public AjaxOnlyAttribute()
        : base(typeof(AjaxOnlyFilter))
    {
    }
}

