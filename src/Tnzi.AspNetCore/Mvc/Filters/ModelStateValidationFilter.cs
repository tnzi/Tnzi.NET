
namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// 模型验证过滤器，自动验证模型状态
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
            // 获取所有验证错误（字典格式：字段名 -> 错误列表）
            var errors = new Dictionary<string, List<string>>();
            
            foreach (var keyValuePair in context.ModelState)
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
            
            context.Result = new BadRequestObjectResult(
                ApiResult<object>.Error(
                    "Validation failed",
                    400,
                    null,
                    errors));
            return;
        }

        await next();
    }
}