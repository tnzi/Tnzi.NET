
namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 请求验证中间件
/// 验证请求的时间戳、签名和 Nonce，防止重放攻击
/// </summary>
public class RequestValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestValidationMiddleware> _logger;

    /// <summary>
    /// 初始化一个<see cref="RequestValidationMiddleware"/>类型的新实例
    /// </summary>
    /// <remarks>
    /// ★★ <see cref="IRequestValidator"/> 刻意<b>不</b>在这里注入：约定式中间件由根容器构造、进程内只有一个实例，
    /// 而校验器注册为 Scoped。构造注入在开发环境（ValidateScopes）让管线构建当场抛异常，
    /// 在生产环境则让一个校验器被根作用域捕获、服务所有请求。它经 <see cref="InvokeAsync"/> 的参数按请求解析。
    /// </remarks>
    public RequestValidationMiddleware(
        RequestDelegate next,
        ILogger<RequestValidationMiddleware> logger)
    {
        _next = Check.NotNull(next);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 处理请求
    /// </summary>
    /// <param name="context">当前请求</param>
    /// <param name="validator">本请求作用域里的校验器（由 <c>UseMiddleware</c> 从 <c>RequestServices</c> 解析）</param>
    public async Task InvokeAsync(HttpContext context, IRequestValidator validator)
    {
        Check.NotNull(validator);

        // ★ try 只包住**校验本身**，绝不能把 _next 圈进来。
        //
        // 圈进来的后果是这个中间件吞掉整条下游管线的异常并一律答 500：
        // NotFoundException 该 404、ValidationException 该 400 带 errorDetails、
        // ForbiddenException 该 403，全部变成 {code:500,"Request validation error"}。
        // ExceptionHandlingMiddleware 注册在**外层**（管线更早），异常在这里就被吃掉了，
        // 它一次都轮不到。前端按 body.code 分支的逻辑（401 触发刷新令牌、404 走空态）随之失效，
        // 而服务端日志把业务异常记成「验证中间件出错」，排查方向被带偏。
        //
        // 同模块的 RateLimitingMiddleware 把 _next 正确地放在 try 之外 —— 两种写法并存，
        // 说明这里是笔误不是设计。
        try
        {
            var error = await validator.ValidateAsync(context);
            if (error != null)
            {
                _logger.LogWarning(
                    "Request validation failed - Path: {Path}, Method: {Method}, IP: {IP}, Error: {Error}",
                    context.GetRedactedPath(), context.Request.Method, context.Request.GetClientIp(), error);

                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";

                var errorResult = ApiResult.Error(error, 400);
                await context.Response.WriteAsync(JsonSerializer.Serialize(errorResult, TnziJsonDefaults.Options));
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Request validation middleware error - Path: {Path}, Method: {Method}",
                context.GetRedactedPath(), context.Request.Method);

            // 校验器自己出错时拒绝请求：验证跑不完意味着这个请求没被检查过，
            // 放行等于让校验器的失效变成静默放行。
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var errorResult = ApiResult.Error("Request validation error", 500);
            await context.Response.WriteAsync(JsonSerializer.Serialize(errorResult, TnziJsonDefaults.Options));
            return;
        }

        // 下游异常原样上抛，交给外层的 ExceptionHandlingMiddleware 按类型翻译。
        await _next(context);
    }
}