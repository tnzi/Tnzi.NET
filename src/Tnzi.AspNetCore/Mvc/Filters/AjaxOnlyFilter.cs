
namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// Ajax限制过滤器，限制操作只能通过 Ajax 请求访问
/// </summary>
public class AjaxOnlyFilter : IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 检查是否为 Ajax 请求
        if (!context.HttpContext.Request.IsAjaxRequest())
        {
            context.Result = new BadRequestObjectResult(
                ApiResult.Error("This action only supports Ajax requests.", 400));
            return Task.CompletedTask;
        }

        return next();
    }
}