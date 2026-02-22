namespace Tnzi.Authorization.Extensions;

/// <summary>
/// RouteEndpoint 扩展方法
/// 用于从路由信息自动识别权限
/// </summary>
public static class EndpointExtensions
{
    /// <summary>
    /// 从路由端点获取 Area 名称
    /// </summary>
    /// <param name="endpoint">路由端点</param>
    /// <returns>Area 名称，如果不存在则返回 null</returns>
    public static string? GetAreaName(this RouteEndpoint endpoint)
    {
        if (endpoint.RoutePattern.RequiredValues.TryGetValue("area", out var value) && value is string area && !string.IsNullOrWhiteSpace(area))
        {
            return area;
        }
        return null;
    }

    /// <summary>
    /// 从路由端点获取 Controller 名称
    /// </summary>
    /// <param name="endpoint">路由端点</param>
    /// <returns>Controller 名称（已规范化，移除 Controller 和 Base 后缀），如果不存在则返回 null</returns>
    public static string? GetControllerName(this RouteEndpoint endpoint)
    {
        if (endpoint.RoutePattern.RequiredValues.TryGetValue("controller", out var value) && value is string controller)
        {
            return NormalizeControllerName(controller);
        }
        return null;
    }

    /// <summary>
    /// 从路由端点获取 Action 名称
    /// </summary>
    /// <param name="endpoint">路由端点</param>
    /// <returns>Action 名称，如果不存在则返回 null</returns>
    public static string? GetActionName(this RouteEndpoint endpoint)
    {
        if (endpoint.RoutePattern.RequiredValues.TryGetValue("action", out var value) && value is string action)
        {
            return action;
        }
        return null;
    }

    /// <summary>
    /// 从路由端点自动生成权限名称
    /// 格式：{Area}.{Controller}.{Action}
    /// 如果没有 Area，格式为：{Controller}.{Action}
    /// </summary>
    /// <param name="endpoint">路由端点</param>
    /// <returns>权限名称，如果无法生成则返回 null</returns>
    public static string? GeneratePermissionName(this RouteEndpoint endpoint)
    {
        var area = endpoint.GetAreaName();
        var controller = endpoint.GetControllerName();
        var action = endpoint.GetActionName();

        if (string.IsNullOrEmpty(controller) || string.IsNullOrEmpty(action))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(area))
        {
            return $"{area}.{controller}.{action}";
        }

        return $"{controller}.{action}";
    }

    /// <summary>
    /// 规范化 Controller 名称
    /// 移除 "Controller" 后缀，处理 "Base" 后缀（如 UserControllerBase -> User）
    /// </summary>
    /// <param name="controllerName">Controller 名称</param>
    /// <returns>规范化后的 Controller 名称</returns>
    private static string NormalizeControllerName(string controllerName)
    {
        if (string.IsNullOrEmpty(controllerName))
        {
            return controllerName;
        }

        // 移除 "Controller" 后缀
        if (controllerName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
        {
            controllerName = controllerName[..(controllerName.Length - "Controller".Length)];
        }

        // 移除 "Base" 后缀（处理 ControllerBase 继承的情况）
        if (controllerName.EndsWith("Base", StringComparison.OrdinalIgnoreCase))
        {
            controllerName = controllerName[..(controllerName.Length - "Base".Length)];
        }

        return controllerName;
    }
}

