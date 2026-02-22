
namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// SPA 404 处理中间件，对于 SPA 应用，非 API 路由的 404 请求重定向到 index.html
/// </summary>
public class SPANotFoundMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _apiPathPrefix;

    public SPANotFoundMiddleware(RequestDelegate next, IOptions<AspNetCoreOptions> options)
    {
        _next = Check.NotNull(next);
        
        var aspNetCoreOptions = options?.Value ?? new AspNetCoreOptions();
        _apiPathPrefix = aspNetCoreOptions.ApiPathPrefix ?? "/api";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        // 检查状态码是否为 404
        if (context.Response.StatusCode == StatusCodes.Status404NotFound)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            // 排除 API 路由
            if (path.StartsWith(_apiPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 排除有扩展名的文件请求（静态资源）
            if (Path.HasExtension(path))
            {
                return;
            }

            // 排除已经重定向过的请求（避免无限循环）
            if (path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 如果响应已经开始发送，则无法重写
            if (context.Response.HasStarted)
                return;

            // 将请求路径重写为 /index.html
            context.Request.Path = "/index.html";

            // 重置状态码，让下一个中间件重新处理
            context.Response.StatusCode = StatusCodes.Status200OK;

            // 重新执行管道
            await _next(context);
        }
    }
}