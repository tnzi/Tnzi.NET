
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
        var options = Options;
        if (options.EnableRequestBodyCapture)
        {
            requestBody = await CaptureRequestBodyAsync(context, options);
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
    /// 采集请求体：先按路径 / Content-Type / Content-Length 闸住，再整条读入，<b>先脱敏、后截断</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 顺序不能反。脱敏要解析完整文档，截断过的 JSON 是非法串、脱敏器只能原样返回：此前先按
    /// <see cref="AuditOptions.MaxRequestBodySize"/> 截断再脱敏，任何超过它的 JSON 体连同其中的口令、API 密钥
    /// 一起原样入表；表单编码的体不是 JSON，无论大小一律原样入表。
    /// </para>
    /// <para>
    /// 于是：只采 JSON 与表单编码两种能按字段名脱敏的体，其余（multipart 上传、二进制、XML、纯文本）只记标记；
    /// 超过 <see cref="AuditOptions.MaxRequestBodySizeLimit"/> 的体不读、只记标记（记一半等于不脱敏）；
    /// 标成 JSON 却解析不了的体同样只记标记。缓冲在这些闸门<b>之后</b>才开：一个上传不该为了
    /// 「记几 KB 二进制」整条落一份临时文件。认证端点整条不采，与请求日志同一条规则
    /// （<see cref="LoggedBodySanitizer.AllowsBodyCapture"/>）：那些端点上「哪个字段是凭据」猜不全。
    /// </para>
    /// </remarks>
    private async Task<string?> CaptureRequestBodyAsync(HttpContext context, AuditOptions options)
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

        if (!LoggedBodySanitizer.AllowsBodyCapture(request.Path))
        {
            return null;
        }

        var format = DetectBodyFormat(request.ContentType);
        if (format == CapturedBodyFormat.Unsupported)
        {
            // Content-Type 是调用方给的头，标记同样要按列宽裁：超列宽的一行会让整批审计 INSERT 失败。
            return AuditOperationColumns.Fit(
                $"[request body not captured: {request.ContentType?.Trim()}]",
                AuditOperationColumns.RequestBodyMaxLength);
        }

        if (request.ContentLength > AuditOptions.MaxRequestBodySizeLimit)
        {
            return RequestBodyExceededMarker;
        }

        try
        {
            // 有界读：至多读上界 + 1 字节，读完把流位置放回 0，下游模型绑定照常拿到整条体。
            // 不可定位的流此刻才开缓冲（不传 bufferLimit：那是「请求体总大小上限」，超过即在下游读取时抛异常）。
            var body = await request.TryReadAsStringAsync(AuditOptions.MaxRequestBodySizeLimit, context.RequestAborted);
            if (body == null)
            {
                return RequestBodyExceededMarker;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var redacted = RedactBody(body, format, options);
            if (redacted == null)
            {
                return RequestBodyUnparseableMarker;
            }

            // 按配置截断（已脱敏的文本），再按存储列宽二次截断：MaxRequestBodySize 允许配到 64KB，
            // 而 RequestBody 列是 8192（见 AuditOperationColumns）：不截断会让整批审计 INSERT 失败，
            // 后台服务只记一条错误日志，本批审计全部丢失。
            // 第一次截断同样不能切开代理对：孤立的高代理项后面接上省略号后缀，列宽那一刀只看结尾、清不掉它，
            // 而 PostgreSQL 照样拒绝整批 INSERT（Truncate 在截断点落在代理对中间时少截一个字符）
            var recorded = redacted.Truncate(options.MaxRequestBodySize, TruncationSuffix);
            return AuditOperationColumns.Fit(recorded, AuditOperationColumns.RequestBodyMaxLength);
        }
        catch (Exception ex)
        {
            // Always reset position so downstream can read the body even if capture fails
            try { request.Body.Position = 0; } catch { /* stream may not be seekable */ }
            _logger.LogDebug(ex, "Failed to capture request body for audit");
            return null;
        }
    }

    /// <summary>超过读入上界时记下的标记。</summary>
    internal const string RequestBodyExceededMarker = "[request body exceeded capture limit]";

    /// <summary>标成 JSON 却解析不了、因而无法脱敏时记下的标记。</summary>
    internal const string RequestBodyUnparseableMarker = "[request body not captured: not valid JSON]";

    private const string TruncationSuffix = "... (truncated)";

    private enum CapturedBodyFormat
    {
        Json,
        Form,
        Unsupported,
    }

    /// <summary>
    /// 只有能按字段名脱敏的两种体可采：JSON（含 <c>+json</c> 变体）与表单编码。没有 Content-Type 的体按 JSON 试解析，解析不了只记标记。
    /// </summary>
    private static CapturedBodyFormat DetectBodyFormat(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return CapturedBodyFormat.Json;
        }

        var separator = contentType.IndexOf(';');
        var mediaType = (separator >= 0 ? contentType[..separator] : contentType).Trim();

        if (mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return CapturedBodyFormat.Form;
        }

        return mediaType.EndsWith("/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
            ? CapturedBodyFormat.Json
            : CapturedBodyFormat.Unsupported;
    }

    /// <summary>
    /// 按字段名脱敏一整条体；JSON 解析不了时返回 <c>null</c>（调用方记标记，绝不原样入表）。
    /// </summary>
    private string? RedactBody(string body, CapturedBodyFormat format, AuditOptions options)
    {
        // 标成表单、实为 JSON 的体（客户端标错类型很常见）仍按 JSON 脱敏：表单解析会把整段 JSON 当成一个键。
        var trimmed = body.TrimStart();
        var looksLikeJson = trimmed.StartsWith('{') || trimmed.StartsWith('[');

        if (format == CapturedBodyFormat.Form && !looksLikeJson)
        {
            var parsed = QueryHelpers.ParseQuery(body);
            var redacted = QueryStringRedactor.Redact(parsed, body, FormSensitiveKeys(options));

            // 命中时重建的串带前导 '?'（查询串形态），表单体没有它。
            return ReferenceEquals(redacted, body) ? body : redacted?.TrimStart('?');
        }

        return _redactor.TryRedact(body, options.SensitiveFields, out var redactedJson) ? redactedJson : null;
    }

    /// <summary>
    /// 表单的敏感键：请求体字段名（<see cref="AuditOptions.SensitiveFields"/>，camelCase）与查询参数名
    /// （<see cref="AuditOptions.SensitiveQueryKeys"/>，OAuth 的 snake_case）两份名单的并集：表单两种写法都有。
    /// </summary>
    private static HashSet<string> FormSensitiveKeys(AuditOptions options)
    {
        var keys = new HashSet<string>(options.SensitiveFields, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(options.SensitiveQueryKeys);
        return keys;
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
        var options = Options;

        // 路径里值命中敏感名单的路由参数（签署 / 分享链接的 {token}）换成掩码：令牌本身就是能用的凭据，
        // 与查询串同一份名单（SensitiveQueryKeys）。功能名的回退与 Url 列都用它，不用 Request.Path。
        // ★ 这里取转义形式（PathString.ToString()），不走 GetRedactedPath 的解码形式（PathString.Value）：
        // Url 列存「路径 + 查询串」，查询串本身是转义的（原样或经 QueryStringRedactor 重建）；路径若存解码形式，
        // 段里的 %3F / %23 会解成 ? / #，一列里两种编码混存，路径与查询串的分界随之说不清。
        // 转义形式也是这一列历来的形状，升级前后同一个端点的行不会分成两种写法。
        var redactedPath = RequestPathRedactor.Redact(
                context.Request.Path.ToString(), context.Request.RouteValues, options.SensitiveQueryKeys)
            ?? string.Empty;

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
        var functionName = $"{context.Request.Method} {redactedPath}";
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

        // 获取请求参数（受配置控制）。★ 两处都要脱敏：表单字段按 SensitiveFields ∪ SensitiveQueryKeys
        // （表单里两种写法都有：password 与 api_key / access_token；只按一份掩，另一份的键原样入表），
        // 查询参数按 SensitiveQueryKeys —— 此前整个 Query 原样序列化，?token= / ?sig= 的原值随之入表。
        string? requestParameters = null;
        if (options.EnableRequestParameters)
        {
            try
            {
                if (context.Request.HasFormContentType && context.Request.Form.Count > 0)
                {
                    var formKeys = FormSensitiveKeys(options);
                    var formDict = context.Request.Form.ToDictionary(
                        f => f.Key,
                        f => formKeys.Contains(f.Key) ? RequestBodyRedactor.RedactedValue : f.Value.ToString());
                    requestParameters = JsonSerializer.Serialize(formDict);
                }
                else if (context.Request.Query.Count > 0)
                {
                    var queryDict = context.Request.Query.ToDictionary(
                        q => q.Key,
                        q => QueryStringRedactor.IsSensitive(q.Key, options.SensitiveQueryKeys) ? QueryStringRedactor.RedactedValue : q.Value.ToString());
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
        // 路径段里的同名路由参数同样掩掉（见上面的 redactedPath）。
        var queryString = QueryStringRedactor.Redact(
            context.Request.Query, context.Request.QueryString.Value, options.SensitiveQueryKeys);

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
                redactedPath + queryString,
                AuditOperationColumns.UrlMaxLength),
            // 本中间件挂在异常处理中间件之内：异常穿过这里时响应还没写，StatusCode 仍是默认 200。
            // 按异常处理器同一张表折算成客户端实际收到的码，否则服务层抛的 403 / 404 以「失败 + 200」入表；
            // 客户端断开是 499，响应已开始后才抛出的是已经发出的那个码。
            HttpStatusCode = exception is null ? context.Response.StatusCode : ExceptionHttpStatusCode.Resolve(context, exception),
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
