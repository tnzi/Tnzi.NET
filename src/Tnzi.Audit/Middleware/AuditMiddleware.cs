
namespace Tnzi.Audit.Middleware;

/// <summary>
/// 审计中间件
/// </summary>
public class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditMiddleware> _logger;
    private readonly IAuditSender _auditSender;
    private readonly AuditOptions _auditOptions;

    public AuditMiddleware(
        RequestDelegate next,
        ILogger<AuditMiddleware> logger,
        IAuditSender auditSender,
        IOptions<AuditOptions> auditOptions)
    {
        _next = Check.NotNull(next);
        _logger = Check.NotNull(logger);
        _auditSender = Check.NotNull(auditSender);
        _auditOptions = Check.NotNull(auditOptions).Value;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IUserAgentParserService? userAgentParser = null)
    {
        // 检查是否启用操作审计
        if (!_auditOptions.EnableOperationAudit)
        {
            await _next(context);
            return;
        }

        // 检查排除路径
        if (IsExcludedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var startTime = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            try
            {
                await EnqueueAuditOperationAsync(context, currentUser, userAgentParser, startTime, stopwatch.ElapsedMilliseconds, exception);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue audit operation");
            }
        }
    }

    private bool IsExcludedPath(PathString path)
    {
        foreach (var excluded in _auditOptions.ExcludedPaths)
        {
            if (path.StartsWithSegments(excluded))
            {
                return true;
            }
        }
        return false;
    }

    private async Task EnqueueAuditOperationAsync(
        HttpContext context,
        ICurrentUser currentUser,
        IUserAgentParserService? userAgentParser,
        DateTime startTime,
        long duration,
        Exception? exception)
    {
        // 解析 UserAgent
        var userAgent = context.Request.Headers["User-Agent"].ToString();
        string? operatingSystem = null;
        string? browser = null;

        if (userAgentParser != null && !string.IsNullOrEmpty(userAgent))
        {
            var uaInfo = userAgentParser.Parse(userAgent);
            if (uaInfo != null)
            {
                operatingSystem = uaInfo.OperatingSystem;
                browser = uaInfo.Browser;
            }
        }

        // 获取功能名（从路由信息）
        var functionName = $"{context.Request.Method} {context.Request.Path}";
        var routeData = context.Request.RouteValues;
        if (routeData.TryGetValue("controller", out var controller) &&
            routeData.TryGetValue("action", out var action))
        {
            functionName = $"{controller}.{action}";
        }

        // 获取请求参数（受配置控制）
        string? requestParameters = null;
        if (_auditOptions.EnableRequestParameters)
        {
            try
            {
                if (context.Request.HasFormContentType && context.Request.Form.Count > 0)
                {
                    var formDict = context.Request.Form.ToDictionary(f => f.Key, f => f.Value.ToString());
                    requestParameters = System.Text.Json.JsonSerializer.Serialize(formDict);
                }
                else if (context.Request.Query.Count > 0)
                {
                    var queryDict = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
                    requestParameters = System.Text.Json.JsonSerializer.Serialize(queryDict);
                }
            }
            catch
            {
                // 忽略参数序列化错误
            }
        }

        var auditOperation = new AuditOperation
        {
            FunctionName = functionName,
            UserId = currentUser.Id,
            UserName = currentUser.UserName,
            NickName = currentUser.UserName,
            Ip = context.Connection.RemoteIpAddress?.ToString(),
            OperatingSystem = operatingSystem,
            Browser = browser,
            UserAgent = userAgent,
            ResultType = exception == null && context.Response.StatusCode < 400
                ? AuditResultType.Success
                : AuditResultType.Failed,
            Message = exception?.Message ?? (context.Response.StatusCode >= 400 ? "Request failed" : "Success"),
            Elapsed = duration,
            HttpMethod = context.Request.Method,
            Url = context.Request.Path + context.Request.QueryString,
            HttpStatusCode = context.Response.StatusCode,
            Exception = exception?.ToString(),
            RequestParameters = requestParameters,
            TenantId = currentUser.TenantId,
            StartTime = startTime,
            EndTime = startTime.AddMilliseconds(duration)
        };

        await _auditSender.SendAsync(auditOperation);
    }
}
