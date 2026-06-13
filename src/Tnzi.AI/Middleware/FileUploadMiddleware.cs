namespace Tnzi.AI.Middleware;

/// <summary>
/// 文件上传感知中间件 — 将已上传文件信息注入为系统消息
/// </summary>
public class FileUploadMiddleware : IAiMiddleware
{
    /// <summary>
    /// 上下文属性键：线程上传目录路径。
    /// 兼容两种来源：ThreadDataMiddleware 设置的 ThreadDataState 对象，或直接设置的字符串路径。
    /// </summary>
    public const string ThreadUploadsDirKey = "ThreadUploadsDir";

    /// <summary>最大单文件名长度</summary>
    private const int MaxFileNameLength = 255;

    private readonly ILogger<FileUploadMiddleware> _logger;

    public int Order => AiMiddlewareOrders.FileUpload;

    public FileUploadMiddleware(ILogger<FileUploadMiddleware> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        InjectFileContext(context);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        InjectFileContext(context);

        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }
    }

    private void InjectFileContext(AiMiddlewareContext context)
    {
        var attachments = context.Request.Attachments;
        if (attachments is not { Count: > 0 })
            return;

        var uploadsDir = ResolveUploadsDir(context);
        if (uploadsDir == null)
        {
            _logger.LogDebug("No uploads directory available, skipping file upload injection");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("<uploaded_files>");

        var injectedCount = 0;

        var normalizedUploadsDir = Path.GetFullPath(uploadsDir);

        foreach (var attachment in attachments)
        {
            // 安全检查：文件名长度和路径遍历
            if (string.IsNullOrWhiteSpace(attachment.FileName) || attachment.FileName.Length > MaxFileNameLength)
            {
                _logger.LogWarning("Rejected invalid file name: {FileName}", attachment.FileName);
                continue;
            }

            var filePath = Path.GetFullPath(Path.Combine(uploadsDir, attachment.FileName));

            // 路径包含检查：确保解析后的路径仍在上传目录内
            if (!filePath.StartsWith(normalizedUploadsDir, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Rejected file with path traversal: {FileName}", attachment.FileName);
                continue;
            }

            if (!File.Exists(filePath))
            {
                _logger.LogDebug("Uploaded file not found on disk: {FileName}", attachment.FileName);
                continue;
            }

            var escapedName = System.Security.SecurityElement.Escape(attachment.FileName);
            var escapedType = System.Security.SecurityElement.Escape(attachment.ContentType ?? "application/octet-stream");
            sb.AppendLine($"<file name=\"{escapedName}\" size=\"{attachment.Size}\" content_type=\"{escapedType}\">");
            sb.AppendLine($"  <path>{System.Security.SecurityElement.Escape(filePath)}</path>");
            sb.AppendLine("</file>");
            injectedCount++;
        }

        sb.AppendLine("</uploaded_files>");

        if (injectedCount == 0)
            return;

        // 在第一条 User 消息之前插入系统消息
        var systemMessage = new ChatMessage(ChatRole.System, sb.ToString());
        var firstUserIndex = context.Messages.FindIndex(m => m.Role == ChatRole.User);

        if (firstUserIndex >= 0)
            context.Messages.Insert(firstUserIndex, systemMessage);
        else
            context.Messages.Insert(0, systemMessage);

        _logger.LogDebug("Injected {Count} uploaded file references into context", injectedCount);
    }

    /// <summary>
    /// 从上下文中解析上传目录路径，支持 ThreadDataState 对象或直接字符串两种来源
    /// </summary>
    private static string? ResolveUploadsDir(AiMiddlewareContext context)
    {
        // 优先：直接设置的字符串路径
        if (context.Properties.TryGetValue(ThreadUploadsDirKey, out var dirObj) && dirObj is string dir)
            return dir;

        // 回退：从 ThreadDataState 中提取（Sandbox 模块设置的 "ThreadData" 键）
        if (context.Properties.TryGetValue("ThreadData", out var tdObj) && tdObj is { } threadData)
        {
            // 使用反射避免硬依赖 Tnzi.AI.Sandbox 程序集
            var uploadsPath = threadData.GetType().GetProperty("UploadsPath")?.GetValue(threadData) as string;
            if (!string.IsNullOrEmpty(uploadsPath))
                return uploadsPath;
        }

        return null;
    }
}
