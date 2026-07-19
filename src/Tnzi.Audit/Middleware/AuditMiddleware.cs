
namespace Tnzi.Audit.Middleware;

/// <summary>
/// 审计中间件
/// </summary>
public class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditMiddleware> _logger;
    private readonly IAuditSender _auditSender;
    private readonly IOptionsMonitor<AuditOptions> _optionsMonitor;
    private readonly RequestBodyRedactor _redactor;

    // 经 IOptionsMonitor 在使用点热读取，使 admin 配置中心对 AuditOptions 的改动即时生效
    // （中间件在管线中仅实例化一次，若在构造期固化快照将永久冻结在启动值）。
    private AuditOptions Options => _optionsMonitor.CurrentValue;

    public AuditMiddleware(
        RequestDelegate next,
        ILogger<AuditMiddleware> logger,
        IAuditSender auditSender,
        IOptionsMonitor<AuditOptions> auditOptions,
        RequestBodyRedactor redactor)
    {
        _next = Check.NotNull(next);
        _logger = Check.NotNull(logger);
        _auditSender = Check.NotNull(auditSender);
        _optionsMonitor = Check.NotNull(auditOptions);
        _redactor = Check.NotNull(redactor);
    }

    // 中间件是 pipeline 单例，per-request 服务（ICurrentUser / IEntityAuditCollector 等）
    // 经 InvokeAsync 参数由框架从 context.RequestServices 逐请求解析
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IEntityAuditCollector entityAuditCollector, IUserAgentParserService? userAgentParser = null)
    {
        // 操作审计请求门（总开关 / 排除路径 / [AuditDisabled]）——
        // 与 EntityAuditSaveChangesInterceptor 的采集门共用 AuditOperationGate 判定
        if (!AuditOperationGate.ShouldAudit(context, Options))
        {
            await _next(context);
            return;
        }

        // Enable request body buffering if capture is enabled
        string? requestBody = null;
        if (Options.EnableRequestBodyCapture)
        {
            requestBody = await CaptureRequestBodyAsync(context);
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
                await EnqueueAuditOperationAsync(context, currentUser, entityAuditCollector, userAgentParser, startTime, stopwatch.ElapsedMilliseconds, exception, requestBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue audit operation");
            }
        }
    }

    /// <summary>
    /// Capture and redact request body content.
    /// Enables buffering so the body can be read by downstream middleware/controllers.
    /// </summary>
    private async Task<string?> CaptureRequestBodyAsync(HttpContext context)
    {
        var request = context.Request;

        // Only capture for methods that typically have a body
        if (request.Method is "GET" or "HEAD" or "OPTIONS" or "DELETE")
        {
            return null;
        }

        if (request.ContentLength is null or 0)
        {
            return null;
        }

        try
        {
            var maxSize = Options.MaxRequestBodySize;
            request.EnableBuffering(bufferLimit: maxSize);

            var buffer = new byte[Math.Min(request.ContentLength ?? maxSize, maxSize)];
            var bytesRead = await request.Body.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false);

            // Reset position so downstream can read the body
            request.Body.Position = 0;

            if (bytesRead == 0)
            {
                return null;
            }

            var body = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // Redact sensitive fields
            if (Options.SensitiveFields.Count > 0)
            {
                body = _redactor.Redact(body, Options.SensitiveFields);
            }

            return body;
        }
        catch (Exception ex)
        {
            // Always reset position so downstream can read the body even if capture fails
            try { request.Body.Position = 0; } catch { /* stream may not be seekable */ }
            _logger.LogDebug(ex, "Failed to capture request body for audit");
            return null;
        }
    }

    private async Task EnqueueAuditOperationAsync(
        HttpContext context,
        ICurrentUser currentUser,
        IEntityAuditCollector entityAuditCollector,
        IUserAgentParserService? userAgentParser,
        DateTime startTime,
        long duration,
        Exception? exception,
        string? requestBody = null)
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

        // 采集时定案写/读分类 + 提取端点权限码（[AuditRead] > 方法级操作码 >
        // 三层门约定 admin 面(类级 .view)无操作码=读 > HTTP 方法+伪读启发式），
        // 查询端不再对新行做字符串猜测
        var (isWrite, permissionName) = AuditOperationClassifier.Classify(context, functionName);

        // 获取请求参数（受配置控制）
        string? requestParameters = null;
        if (Options.EnableRequestParameters)
        {
            try
            {
                if (context.Request.HasFormContentType && context.Request.Form.Count > 0)
                {
                    var formDict = context.Request.Form.ToDictionary(f => f.Key, f => f.Value.ToString());
                    requestParameters = JsonSerializer.Serialize(formDict);
                }
                else if (context.Request.Query.Count > 0)
                {
                    var queryDict = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
                    requestParameters = JsonSerializer.Serialize(queryDict);
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
            PermissionName = permissionName,
            IsWrite = isWrite,
            UserId = currentUser.Id,
            UserName = currentUser.UserName,
            NickName = null, // ICurrentUser 不包含 NickName，避免错误赋值 UserName
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
            RequestBody = requestBody,
            TenantId = currentUser.TenantId,
            StartTime = startTime,
            EndTime = startTime.AddMilliseconds(duration)
        };

        // 挂载本请求内经 EF 拦截器采集的实体级变更（EnableEntityAudit 关闭时恒为空），
        // 随操作审计实体图一起经 channel → 后台批量 InsertMany 级联入库
        foreach (var entityEntry in entityAuditCollector.Drain())
        {
            auditOperation.EntityEntries.Add(entityEntry);
        }

        await _auditSender.SendAsync(auditOperation);
    }
}
