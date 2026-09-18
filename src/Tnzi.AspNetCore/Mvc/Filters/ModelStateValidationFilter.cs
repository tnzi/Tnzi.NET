
namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// 模型验证过滤器，自动验证模型状态。
/// 失败时返回 <see cref="ApiResult{T}"/> 信封 + 400：message 是第一条字段错误原文、errorDetails 是
/// 字段名 → 错误列表的完整字典。message 与 errorDetails 的取法与 <c>ApiControllerBase.ValidationError()</c>
/// 共用同一组 <see cref="MvcExtensions"/> 扩展，手工验证与全局过滤器对同一份 ModelState 给出相同的信封。
/// </summary>
public class ModelStateValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 检查是否标记了忽略验证
        var ignoreValidation = context.ActionDescriptor.EndpointMetadata
            .OfType<ModelStateValidationAttribute>()
            .Any(attr => attr.Ignore);

        if (!ignoreValidation && !context.ModelState.IsValid)
        {
            context.Result = new BadRequestObjectResult(
                ApiResult<object>.Error(
                    context.ModelState.ValidationMessage(),
                    400,
                    null,
                    context.ModelState.GetValidationErrors()));
            return;
        }

        await next();
    }
}