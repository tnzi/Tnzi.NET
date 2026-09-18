namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// <see cref="AspNetCoreModule"/> 固定管线里向别的模块开放的插入点。
/// </summary>
/// <remarks>
/// <para>
/// 模块的 <c>OnApplicationInitializationAsync</c> 按 LoadOrder 执行，而 AspNetCore 模块（LoadOrder 0）已经把
/// 认证 → 限流 → 授权整段挂完了 —— 后来的模块只能追加在<b>授权之后</b>。那个位置只看得见通过了认证、限流与授权的请求：
/// 401 / 403 / 429 由授权与限流中间件就地写出响应、不再调用下游，追加在后面的中间件对它们一无所知。
/// 一个想记「谁被拒绝了」的中间件放在那里等于只记成功。
/// </para>
/// <para>
/// 所以插入点在 <c>ConfigureServicesAsync</c> 阶段经 DI 登记（<c>AddRequestPipelineMiddleware</c>），
/// 由 AspNetCore 模块在挂管线时按阶段展开，登记顺序即挂载顺序。
/// </para>
/// </remarks>
public enum RequestPipelineStage
{
    /// <summary>
    /// 紧跟 <c>UseAuthentication()</c> 之后、限流 / 租户解析 / 授权之前：<c>HttpContext.User</c>（因而 <c>ICurrentUser</c>）已填好，
    /// 而将被限流、未认证、无权限的请求都还没被短路。要记录或观测<b>每一个</b>请求的中间件挂这里。
    /// </summary>
    AfterAuthentication,
}

/// <summary>
/// 一条管线插入登记：在 <see cref="Stage"/> 处执行 <see cref="Configure"/>。经 DI 以多实例注册，AspNetCore 模块按登记顺序展开。
/// </summary>
public sealed class RequestPipelineRegistration
{
    /// <summary>初始化一条登记。</summary>
    public RequestPipelineRegistration(RequestPipelineStage stage, Action<IApplicationBuilder> configure)
    {
        Stage = stage;
        Configure = Check.NotNull(configure);
    }

    /// <summary>插入点。</summary>
    public RequestPipelineStage Stage { get; }

    /// <summary>在插入点上挂中间件的动作（通常是一次 <c>UseMiddleware&lt;T&gt;()</c>）。</summary>
    public Action<IApplicationBuilder> Configure { get; }
}
