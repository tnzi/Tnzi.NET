namespace Tnzi.AspNetCore.Mvc.Conventions;

/// <summary>
/// DI 标记类，表示 HostingModule 已激活默认 Controller
/// 当此标记存在于 DI 容器中时，<see cref="DefaultControllerFilterProvider"/> 保留 [DefaultController] 标记的 Controller；
/// 当此标记不存在时，移除所有 [DefaultController] 标记的 Controller。
/// </summary>
public class DefaultControllerEnabledMarker;

/// <summary>
/// 默认 Controller 过滤提供者
/// 检查 DI 容器中是否注册了 <see cref="DefaultControllerEnabledMarker"/>。
/// 若未注册，则移除所有带 <see cref="DefaultControllerAttribute"/> 标记的 Controller。
/// </summary>
public class DefaultControllerFilterProvider : IApplicationModelProvider
{
    private readonly bool _isEnabled;

    /// <summary>
    /// 执行顺序，在 ConditionalControllerProvider(-500) 之前执行
    /// </summary>
    public int Order => -600;

    /// <summary>
    /// 初始化默认 Controller 过滤提供者
    /// </summary>
    /// <param name="services">服务集合，用于检查 <see cref="DefaultControllerEnabledMarker"/> 是否已注册</param>
    public DefaultControllerFilterProvider(IServiceCollection services)
    {
        Check.NotNull(services);
        _isEnabled = services.Any(s => s.ServiceType == typeof(DefaultControllerEnabledMarker));
    }

    /// <inheritdoc />
    public void OnProvidersExecuting(ApplicationModelProviderContext context)
    {
        // 如果 HostingModule 已激活默认 Controller，则不过滤
        if (_isEnabled)
            return;

        var controllersToRemove = new List<ControllerModel>();

        foreach (var controller in context.Result.Controllers)
        {
            if (controller.ControllerType.GetCustomAttribute<DefaultControllerAttribute>() != null)
            {
                controllersToRemove.Add(controller);
            }
        }

        foreach (var controller in controllersToRemove)
        {
            context.Result.Controllers.Remove(controller);
        }
    }

    /// <inheritdoc />
    public void OnProvidersExecuted(ApplicationModelProviderContext context)
    {
    }
}
