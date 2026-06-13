namespace Tnzi.AI.Middleware;

/// <summary>
/// 图片查看中间件 — Before only: 将已查看的图片注入为多模态消息内容。
/// </summary>
/// <remarks>
/// 在 ViewImageTool 将图片存入 context.Properties["ViewedImages"] 后，
/// 此中间件将图片作为 DataContent 注入到 context.Messages 中，
/// 使 AI 模型能够在下一轮对话中看到图片内容。
/// </remarks>
public class ViewImageMiddleware : IAiMiddleware
{
    private readonly ILogger<ViewImageMiddleware> _logger;

    public int Order => AiMiddlewareOrders.ViewImage;

    public ViewImageMiddleware(ILogger<ViewImageMiddleware> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        InjectViewedImages(context);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context,
        AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        InjectViewedImages(context);

        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 将已查看的图片注入为多模态用户消息
    /// </summary>
    private void InjectViewedImages(AiMiddlewareContext context)
    {
        // 检查是否有已查看的图片
        if (!context.Properties.TryGetValue(ViewImageTool.ViewedImagesKey, out var value)
            || value is not List<(string MimeType, string Base64)> images
            || images.Count == 0)
        {
            return;
        }

        // 去重：已注入则跳过
        if (context.Properties.TryGetValue(ViewImageTool.ViewedImagesInjectedKey, out var injected)
            && injected is true)
        {
            _logger.LogDebug("Viewed images already injected, skipping");
            return;
        }

        // 构建多模态消息内容
        var contents = new List<AIContent>();

        foreach (var (mimeType, base64) in images)
        {
            try
            {
                var imageBytes = Convert.FromBase64String(base64);
                contents.Add(new DataContent(imageBytes, mimeType));
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "Failed to decode Base64 image data, skipping");
            }
        }

        if (contents.Count == 0)
            return;

        contents.Insert(0, new TextContent($"The user has shared {contents.Count} image(s) for analysis:"));

        var message = new ChatMessage(ChatRole.User, contents);
        context.Messages.Add(message);

        // 标记为已注入
        context.Properties[ViewImageTool.ViewedImagesInjectedKey] = true;

        _logger.LogDebug("Injected {Count} viewed image(s) as multimodal message", images.Count);
    }
}
