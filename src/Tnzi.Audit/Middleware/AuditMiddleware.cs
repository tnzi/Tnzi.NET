
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

            // 不能传 bufferLimit：那是「请求体总大小上限」，超过即在读取时抛 IOException。
            // 用 MaxRequestBodySize 当上限会让所有大于该值的请求在下游模型绑定时 500 ——
            // 而本配置的语义是「审计只记录前 N 字节，超出部分截断」，不是拒绝请求。
            request.EnableBuffering();

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

            // 按存储列宽二次截断：MaxRequestBodySize 允许配到 64KB，而 RequestBody 列是
            // 8192（见 AuditOperationColumns）——不截断会让整批审计 INSERT 失败，
            // 后台服务只记一条错误日志，本批审计全部丢失。
            return AuditOperationColumns.Fit(body, AuditOperationColumns.RequestBodyMaxLength);
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

        // 获取请求参数（受配置控制）。★ 两处都要脱敏：表单字段按 SensitiveFields（它们就是请求体字段），
        // 查询参数按 SensitiveQueryKeys —— 此前整个 Query 原样序列化，?token= / ?sig= 的原值随之入表。
        string? requestParameters = null;
        if (Options.EnableRequestParameters)
        {
            try
            {
                if (context.Request.HasFormContentType && context.Request.Form.Count > 0)
                {
                    var formDict = context.Request.Form.ToDictionary(
                        f => f.Key,
                        f => Options.SensitiveFields.Contains(f.Key) ? RequestBodyRedactor.RedactedValue : f.Value.ToString());
                    requestParameters = JsonSerializer.Serialize(formDict);
                }
                else if (context.Request.Query.Count > 0)
                {
                    var queryDict = context.Request.Query.ToDictionary(
                        q => q.Key,
                        q => QueryStringRedactor.IsSensitive(q.Key, Options.SensitiveQueryKeys) ? QueryStringRedactor.RedactedValue : q.Value.ToString());
                    requestParameters = JsonSerializer.Serialize(queryDict);
                }
            }
            catch
            {
                // 忽略参数序列化错误
            }
        }

        // Url 列存 Path + QueryString。查询串里的凭据（access_token / sig / token / enrollmentToken /
        // password）按 SensitiveQueryKeys 换成掩码再存：路径排除盖不住这些端点，它们的请求本身正是要审计的操作。
        var queryString = QueryStringRedactor.Redact(
            context.Request.Query, context.Request.QueryString.Value, Options.SensitiveQueryKeys);

        // ★ 每个来自请求（或由请求派生）的字符串都按列宽裁一刀。整批审计用一条 InsertMany
        // 落库，任何一行超列宽，SQL Server / PostgreSQL 会拒绝整条 INSERT，后台服务记一行
        // 日志后整批丢弃 —— 一个匿名客户端发一个 600 字节的 User-Agent 就能连带抹掉同一时间窗
        // 里其他所有人的审计记录。RequestBody 早已这样处理（见 CaptureRequestBodyAsync），
        // 这里把同一条推理补到其余用户可控字段上。SQLite 不检查长度，测试全绿证明不了这件事。
        var auditOperation = new AuditOperation
        {
            FunctionName = AuditOperationColumns.Fit(functionName, AuditOperationColumns.FunctionNameMaxLength)!,
            PermissionName = AuditOperationColumns.Fit(permissionName, AuditOperationColumns.PermissionNameMaxLength),
            IsWrite = isWrite,
            UserId = currentUser.Id,
            UserName = AuditOperationColumns.Fit(currentUser.UserName, AuditOperationColumns.UserNameMaxLength),
            NickName = null, // ICurrentUser 不包含 NickName，避免错误赋值 UserName
            // 走 GetClientIp 而不是直接读 Connection：它支持反向代理，
            // 且是隐私开关 AspNetCoreOptions.CollectClientIpAddress 的唯一判定点——
            // 关闭采集的部署，审计操作日志同样不该留下地址。
            Ip = AuditOperationColumns.Fit(context.Request.GetClientIp(), AuditOperationColumns.IpMaxLength),
            OperatingSystem = AuditOperationColumns.Fit(operatingSystem, AuditOperationColumns.OperatingSystemMaxLength),
            Browser = AuditOperationColumns.Fit(browser, AuditOperationColumns.BrowserMaxLength),
            UserAgent = AuditOperationColumns.Fit(userAgent, AuditOperationColumns.UserAgentMaxLength),
            ResultType = exception == null && context.Response.StatusCode < 400
                ? AuditResultType.Success
                : AuditResultType.Failed,
            // Exception 列没有上限，完整消息仍在那里；Message 只是摘要。
            Message = AuditOperationColumns.Fit(
                exception?.Message ?? (context.Response.StatusCode >= 400 ? "Request failed" : "Success"),
                AuditOperationColumns.MessageMaxLength),
            Elapsed = duration,
            HttpMethod = AuditOperationColumns.Fit(context.Request.Method, AuditOperationColumns.HttpMethodMaxLength),
            Url = AuditOperationColumns.Fit(
                context.Request.Path + queryString,
                AuditOperationColumns.UrlMaxLength),
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
