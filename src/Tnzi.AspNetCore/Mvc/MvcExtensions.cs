
namespace Tnzi.AspNetCore.Mvc;

/// <summary>
/// MVC相关扩展方法
/// </summary>
public static class MvcExtensions
{
    /// <summary>
    /// 判断类型是否是Controller
    /// </summary>
    /// <param name="type">类型</param>
    /// <param name="isAbstract">是否包含抽象类，默认false</param>
    /// <returns>是否是Controller</returns>
    public static bool IsController(this Type type, bool isAbstract = false)
    {
        Check.NotNull(type);

        return IsController(type.GetTypeInfo(), isAbstract);
    }

    /// <summary>
    /// 判断类型是否是Controller
    /// </summary>
    /// <param name="typeInfo">类型信息</param>
    /// <param name="isAbstract">是否包含抽象类，默认false</param>
    /// <returns>是否是Controller</returns>
    public static bool IsController(this TypeInfo typeInfo, bool isAbstract = false)
    {
        Check.NotNull(typeInfo);

        return typeInfo.IsClass &&
               (isAbstract || !typeInfo.IsAbstract) &&
               !typeInfo.IsNestedPrivate &&
               !typeInfo.ContainsGenericParameters &&
               !typeInfo.IsDefined(typeof(NonControllerAttribute)) &&
               (typeInfo.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) ||
                typeInfo.IsDefined(typeof(ControllerAttribute)));
    }

    /// <summary>
    /// 获取Area名
    /// </summary>
    /// <param name="context">Action上下文</param>
    /// <returns>Area名称</returns>
    public static string? GetAreaName(this ActionContext context)
    {
        Check.NotNull(context);

        if (context.RouteData.Values.TryGetValue("area", out var value) && value is string area && !string.IsNullOrWhiteSpace(area))
        {
            return area;
        }

        return null;
    }

    /// <summary>
    /// 获取Controller名
    /// </summary>
    /// <param name="context">Action上下文</param>
    /// <returns>Controller名称</returns>
    public static string? GetControllerName(this ActionContext context)
    {
        Check.NotNull(context);

        if (context.RouteData.Values.TryGetValue("controller", out var value) && value is string controller)
        {
            return controller;
        }

        return null;
    }

    /// <summary>
    /// 获取Action名
    /// </summary>
    /// <param name="context">Action上下文</param>
    /// <returns>Action名称</returns>
    public static string? GetActionName(this ActionContext context)
    {
        Check.NotNull(context);

        if (context.RouteData.Values.TryGetValue("action", out var value) && value is string action)
        {
            return action;
        }

        return null;
    }

    /// <summary>
    /// 获取第一个验证错误
    /// </summary>
    /// <param name="modelState">模型状态字典</param>
    /// <returns>第一个错误消息</returns>
    public static string? FirstError(this ModelStateDictionary modelState)
    {
        Check.NotNull(modelState);

        foreach (var state in modelState.Values)
        {
            if (state != null && state.Errors.Count > 0)
            {
                var error = state.Errors[0];
                var errorText = error.ErrorMessage;
                
                if (string.IsNullOrWhiteSpace(errorText) && error.Exception != null)
                {
                    errorText = error.Exception.Message;
                }

                if (!string.IsNullOrWhiteSpace(errorText))
                {
                    return errorText;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 获取所有验证错误
    /// </summary>
    /// <param name="modelState">模型状态字典</param>
    /// <returns>错误消息列表</returns>
    public static List<string> Errors(this ModelStateDictionary modelState)
    {
        Check.NotNull(modelState);

        var errors = new List<string>();

        foreach (var keyValuePair in modelState)
        {
            var state = keyValuePair.Value;
            if (state != null && state.Errors.Count > 0)
            {
                foreach (var error in state.Errors)
                {
                    var message = error.ErrorMessage;
                    if (string.IsNullOrWhiteSpace(message) && error.Exception != null)
                    {
                        message = error.Exception.Message;
                    }

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        errors.Add(message);
                    }
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// 获取模型验证错误详情（包含字段名）
    /// </summary>
    /// <param name="modelState">模型状态字典</param>
    /// <returns>字段错误字典</returns>
    public static Dictionary<string, List<string>> GetValidationErrors(this ModelStateDictionary modelState)
    {
        Check.NotNull(modelState);

        var errors = new Dictionary<string, List<string>>();

        foreach (var keyValuePair in modelState)
        {
            var key = keyValuePair.Key;
            var state = keyValuePair.Value;
            
            if (state != null && state.Errors.Count > 0)
            {
                var fieldErrors = new List<string>();
                foreach (var error in state.Errors)
                {
                    var message = error.ErrorMessage;
                    if (string.IsNullOrWhiteSpace(message) && error.Exception != null)
                    {
                        message = error.Exception.Message;
                    }

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        fieldErrors.Add(message);
                    }
                }

                if (fieldErrors.Count > 0)
                {
                    errors[key] = fieldErrors;
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// 获取上传文件的MD5哈希值
    /// </summary>
    /// <param name="file">表单文件</param>
    /// <returns>MD5哈希值</returns>
    public static string GetMd5Hash(this IFormFile file)
    {
        Check.NotNull(file);

        using var stream = file.OpenReadStream();
        return HashHelper.GetMd5(stream);
    }
}