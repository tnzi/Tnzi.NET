
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
    /// 验证失败信封 message 的兜底文本：ModelState 无效、却没有一条错误带可读消息时才用到
    /// （绑定失败只设了 Exception 而 Message 为空就是这种情形）。
    /// </summary>
    private const string DefaultValidationMessage = "Validation failed";

    /// <summary>
    /// 获取第一个验证错误（第一条带可读消息的错误：ErrorMessage 为空时退到 Exception.Message）
    /// </summary>
    /// <param name="modelState">模型状态字典</param>
    /// <returns>第一个错误消息；一条可读的都没有时为 null</returns>
    public static string? FirstError(this ModelStateDictionary modelState)
    {
        Check.NotNull(modelState);

        foreach (var state in modelState.Values)
        {
            if (state == null)
            {
                continue;
            }

            foreach (var error in state.Errors)
            {
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
    /// 验证失败信封的 message：第一条字段错误原文（同 <see cref="FirstError"/>），
    /// 一条可读的都没有时退回 "Validation failed"。
    /// </summary>
    /// <remarks>
    /// 全局 <c>ModelStateValidationFilter</c> 与 <c>ApiControllerBase.ValidationError()</c> 都从这里取 message，
    /// 两条路径对同一份 ModelState 给出逐字相同的答案。消费方的错误提示通常只显示 message、不读
    /// errorDetails —— 此前过滤器一律回固定的 "Validation failed"，用户看到的提示里没有任何字段名可据以行动。
    /// 刻意不带字段键前缀（<c>Directory.People[0].FirstName:</c> 是属性路径，给开发者看的）：
    /// DataAnnotations 的默认消息本身就点名字段，自定义 ErrorMessage 是 DTO 作者写给人读的，字段键留在 errorDetails 里。
    /// </remarks>
    /// <param name="modelState">模型状态字典</param>
    /// <returns>信封 message，恒非空</returns>
    public static string ValidationMessage(this ModelStateDictionary modelState)
    {
        return modelState.FirstError() ?? DefaultValidationMessage;
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