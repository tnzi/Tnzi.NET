
namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 请求验证中间件
/// 验证请求的时间戳、签名和 Nonce，防止重放攻击
/// </summary>
public class RequestValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRequestValidator _validator;
    private readonly ILogger<RequestValidationMiddleware> _logger;

    /// <summary>
    /// 初始化一个<see cref="RequestValidationMiddleware"/>类型的新实例
    /// </summary>
    public RequestValidationMiddleware(
        RequestDelegate next,
        IRequestValidator validator,
        ILogger<RequestValidationMiddleware> logger)
    {
        _next = next;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>
    /// 处理请求
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // 验证请求
            var error = await _validator.ValidateAsync(context);
            if (error != null)
            {
                _logger.LogWarning(
                    "Request validation failed - Path: {Path}, Method: {Method}, IP: {IP}, Error: {Error}",
                    context.Request.Path, context.Request.Method, context.Request.GetClientIp(), error);
                
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                
                // 使用更安全的 JSON 序列化
                var errorResponse = System.Text.Json.JsonSerializer.Serialize(new { error = error });
                await context.Response.WriteAsync(errorResponse);
                return;
            }

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Request validation middleware error - Path: {Path}, Method: {Method}",
                context.Request.Path, context.Request.Method);
            
            // 验证中间件出错时，可以选择拒绝请求或继续处理
            // 这里选择拒绝请求，因为验证失败意味着请求可能不安全
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var errorResponse = System.Text.Json.JsonSerializer.Serialize(new { error = "Request validation error" });
            await context.Response.WriteAsync(errorResponse);
        }
    }
}