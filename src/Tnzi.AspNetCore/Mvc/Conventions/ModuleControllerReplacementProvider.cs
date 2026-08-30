namespace Tnzi.AspNetCore.Mvc.Conventions;

/// <summary>
/// 模块 Controller 替换提供者
/// 按路由模板分组，若同路由存在用户 Controller（无 [DefaultController]）和模块默认 Controller（有 [DefaultController]），
/// 则移除<b>被它替换掉的</b>默认版本，实现用户 Controller 自动覆盖模块默认行为。
/// </summary>
/// <remarks>
/// 替换是<b>定向</b>的：派生自某个默认 Controller 的子类只替换它的基类；
/// 与本组任何默认版本都无继承关系的用户 Controller 才整组接管。
/// 判据与理由见 <see cref="FindReplacer"/>。
/// </remarks>
public class ModuleControllerReplacementProvider : IApplicationModelProvider
{
    private readonly ControllerActivationDiagnostics? _diagnostics;

    /// <summary>
    /// 执行顺序，在 ConditionalControllerProvider(-500) 之后执行
    /// </summary>
    public int Order => -450;

    /// <summary>
    /// 初始化模块 Controller 替换提供者
    /// </summary>
    /// <param name="diagnostics">可选的诊断收集器</param>
    public ModuleControllerReplacementProvider(ControllerActivationDiagnostics? diagnostics = null)
    {
        _diagnostics = diagnostics;
    }

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

        // 检查每个路由组，若有用户 Controller 和 Default Controller 共存，移除被它替换掉的 Default 版本
        var controllersToRemove = new List<ControllerModel>();

        foreach (var group in routeGroups.Values)
        {
            if (group.Count <= 1)
                continue;

            var userControllers = new List<ControllerModel>();
            var defaultControllers = new List<ControllerModel>();

            foreach (var controller in group)
            {
                if (controller.ControllerType.GetCustomAttribute<DefaultControllerAttribute>() != null)
                {
                    defaultControllers.Add(controller);
                }
                else
                {
                    userControllers.Add(controller);
                }
            }

            if (userControllers.Count == 0 || defaultControllers.Count == 0)
                continue;

            var routeTemplate = GetRouteTemplate(group[0]) ?? "unknown";

            foreach (var dc in defaultControllers)
            {
                var replacer = FindReplacer(dc, userControllers, defaultControllers);
                if (replacer == null)
                    continue;

                _diagnostics?.RecordReplacement(
                    dc.ControllerType.FullName ?? dc.ControllerType.Name,
                    replacer.ControllerType.FullName ?? replacer.ControllerType.Name,
                    routeTemplate);

                controllersToRemove.Add(dc);
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
    /// 为一个默认 Controller 找出「替换掉它」的用户 Controller，找不到返回 null（该默认版本保留）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么不能一有用户 Controller 就删掉整组默认版本。</b>
    /// <see cref="DefaultControllerAttribute"/> 是 <c>Inherited = false</c> 而 <c>[Route]</c> 是
    /// <c>Inherited = true</c>：消费方按框架文档写 <c>class XController : DefaultXController</c> 覆写某个端点时，
    /// 子类继承到了路由模板、却继承不到默认标记，于是被判成「用户 Controller」。
    /// 若此时把同路由的默认版本全部删掉，<b>同路由上其它模块的默认 Controller 会被一并删除</b>，
    /// 而子类只靠 C# 继承补回了它自己基类的那些动作，别的模块的端点就此消失（只留一条 Information 日志，
    /// 且日志记的是类名不是丢掉的路由）。
    /// </para>
    /// <para>
    /// 因此替换按「派生关系」定向：用户 Controller 派生自哪个默认 Controller，就只替换哪个。
    /// 一个不派生自本组任何默认 Controller 的用户 Controller，仍按既有约定整组接管
    /// （「在同路由注册自己的 Controller 即可覆盖模块默认」），语义不变。
    /// </para>
    /// </remarks>
    private static ControllerModel? FindReplacer(
        ControllerModel defaultController,
        List<ControllerModel> userControllers,
        List<ControllerModel> defaultControllers)
    {
        // 1. 优先：派生自这个默认 Controller 的子类（框架文档给出的覆写方式）
        foreach (var uc in userControllers)
        {
            if (defaultController.ControllerType.IsAssignableFrom(uc.ControllerType))
                return uc;
        }

        // 2. 其次：完全独立的用户 Controller（不派生自本组任何默认版本）整组接管
        foreach (var uc in userControllers)
        {
            var derivesFromSomeDefault = false;
            foreach (var dc in defaultControllers)
            {
                if (dc.ControllerType.IsAssignableFrom(uc.ControllerType))
                {
                    derivesFromSomeDefault = true;
                    break;
                }
            }

            if (!derivesFromSomeDefault)
                return uc;
        }

        return null;
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
