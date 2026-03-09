namespace Tnzi.AspNetCore.Mvc.Conventions;

/// <summary>
/// 模块 Controller 替换提供者
/// 按路由模板分组，若同路由存在用户 Controller（无 [DefaultController]）和模块默认 Controller（有 [DefaultController]），
/// 则移除默认版本，实现用户 Controller 自动覆盖模块默认行为。
/// </summary>
public class ModuleControllerReplacementProvider : IApplicationModelProvider
{
    /// <summary>
    /// 执行顺序，在 ConditionalControllerProvider(-500) 之后执行
    /// </summary>
    public int Order => -450;

    /// <inheritdoc />
    public void OnProvidersExecuting(ApplicationModelProviderContext context)
    {
        // 按路由模板分组
        var routeGroups = new Dictionary<string, List<ControllerModel>>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in context.Result.Controllers)
        {
            var routeTemplate = GetRouteTemplate(controller);
            if (string.IsNullOrEmpty(routeTemplate))
                continue;

            if (!routeGroups.TryGetValue(routeTemplate, out var group))
            {
                group = [];
                routeGroups[routeTemplate] = group;
            }
            group.Add(controller);
        }

        // 检查每个路由组，若有用户 Controller 和 Default Controller 共存，移除 Default 版本
        var controllersToRemove = new List<ControllerModel>();

        foreach (var group in routeGroups.Values)
        {
            if (group.Count <= 1)
                continue;

            var hasUserController = false;
            var defaultControllers = new List<ControllerModel>();

            foreach (var controller in group)
            {
                if (controller.ControllerType.GetCustomAttribute<DefaultControllerAttribute>() != null)
                {
                    defaultControllers.Add(controller);
                }
                else
                {
                    hasUserController = true;
                }
            }

            // 只有当用户 Controller 存在时才移除 Default Controller
            if (hasUserController && defaultControllers.Count > 0)
            {
                controllersToRemove.AddRange(defaultControllers);
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

    /// <summary>
    /// 获取 Controller 的路由模板
    /// 优先从 [Route] 属性获取，回退到 AttributeRouteModel
    /// </summary>
    private static string? GetRouteTemplate(ControllerModel controller)
    {
        // 从 [Route] 属性获取（使用 GetCustomAttributes 避免多个 Route 时抛异常）
        var routeAttribute = controller.ControllerType.GetCustomAttributes<RouteAttribute>().FirstOrDefault();
        if (routeAttribute != null)
            return routeAttribute.Template;

        // 从 Selectors 获取
        foreach (var selector in controller.Selectors)
        {
            if (selector.AttributeRouteModel?.Template is { } template)
                return template;
        }

        return null;
    }
}
