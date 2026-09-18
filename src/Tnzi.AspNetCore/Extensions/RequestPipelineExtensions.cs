namespace Tnzi.AspNetCore.Extensions;

/// <summary>
/// 让模块在 <c>ConfigureServicesAsync</c> 阶段把中间件登记到 <see cref="AspNetCoreModule"/> 固定管线的某个插入点。
/// </summary>
/// <remarks>
/// 见 <see cref="RequestPipelineStage"/>：在 <c>OnApplicationInitializationAsync</c> 里 <c>UseMiddleware</c> 只能追加在授权之后，
/// 那里看不见 401 / 403 / 429。登记在这里的中间件由 AspNetCore 模块在挂管线时按阶段展开，登记顺序即挂载顺序。
/// </remarks>
public static class RequestPipelineExtensions
{
    /// <summary>
    /// 在 <paramref name="stage"/> 处执行 <paramref name="configure"/>。
    /// </summary>
    public static IServiceCollection AddRequestPipelineMiddleware(this IServiceCollection services, RequestPipelineStage stage, Action<IApplicationBuilder> configure)
    {
        Check.NotNull(services);
        Check.NotNull(configure);

        services.AddSingleton(new RequestPipelineRegistration(stage, configure));
        return services;
    }

    /// <summary>
    /// 在 <paramref name="stage"/> 处 <c>UseMiddleware&lt;TMiddleware&gt;()</c>。
    /// </summary>
    public static IServiceCollection AddRequestPipelineMiddleware<TMiddleware>(this IServiceCollection services, RequestPipelineStage stage)
        where TMiddleware : class
        => services.AddRequestPipelineMiddleware(stage, app => app.UseMiddleware<TMiddleware>());

    /// <summary>
    /// 把登记在 <paramref name="stage"/> 的中间件按登记顺序挂到 <paramref name="app"/> 上。由 AspNetCore 模块在挂管线时调用。
    /// </summary>
    public static IApplicationBuilder UseRequestPipelineStage(this IApplicationBuilder app, RequestPipelineStage stage)
    {
        Check.NotNull(app);

        foreach (var registration in app.ApplicationServices.GetServices<RequestPipelineRegistration>())
        {
            if (registration.Stage == stage)
            {
                registration.Configure(app);
            }
        }

        return app;
    }
}
