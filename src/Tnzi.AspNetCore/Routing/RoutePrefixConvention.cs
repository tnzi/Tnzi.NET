
namespace Tnzi.AspNetCore.Routing;

/// <summary>
/// 全局路由前缀约定
/// 为所有控制器自动添加配置的路由前缀
/// </summary>
public class RoutePrefixConvention : IApplicationModelConvention
{
    private readonly string _routePrefix;
    private readonly AttributeRouteModel _routePrefixModel;

    /// <summary>
    /// 初始化全局路由前缀约定
    /// </summary>
    /// <param name="routePrefix">路由前缀（例如："/api"）</param>
    public RoutePrefixConvention(string routePrefix)
    {
        Check.NotNullOrWhiteSpace(routePrefix);

        // 确保前缀以 / 开头，不以 / 结尾
        _routePrefix = routePrefix.Trim().TrimEnd('/');
        if (!_routePrefix.StartsWith('/'))
        {
            _routePrefix = "/" + _routePrefix;
        }

        _routePrefixModel = new AttributeRouteModel(new RouteAttribute(_routePrefix));
    }

    /// <summary>
    /// 应用路由前缀约定
    /// </summary>
    /// <param name="application">应用模型</param>
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            // 检查控制器是否标记了 [NonRoutePrefix] 特性（如果需要排除某些控制器）
            var nonRoutePrefixAttr = controller.Attributes.OfType<NonRoutePrefixAttribute>().FirstOrDefault();
            if (nonRoutePrefixAttr != null)
            {
                continue;
            }

            foreach (var selector in controller.Selectors.ToList())
            {
                if (selector.AttributeRouteModel != null)
                {
                    var existingRoute = selector.AttributeRouteModel.Template ?? string.Empty;
                    
                    // 如果现有路由已经以配置的前缀开头，则不再添加（避免重复）
                    var normalizedRoute = existingRoute.TrimStart('/');
                    var normalizedPrefix = _routePrefix.TrimStart('/');

                    if (normalizedRoute.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase) ||
                        normalizedRoute.Equals(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 组合路由前缀和现有路由
                    var combinedRoute = _routePrefix + "/" + normalizedRoute;
                    selector.AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(combinedRoute));
                }
                else
                {
                    // 如果没有现有路由，直接使用前缀
                    selector.AttributeRouteModel = _routePrefixModel;
                }
            }
        }
    }
}

/// <summary>
/// 标记控制器不应用全局路由前缀
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class NonRoutePrefixAttribute : Attribute
{
}