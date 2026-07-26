namespace Tnzi.AI.Rag.FileExtractors;

/// <summary>
/// 图片内容提取器 - 使用 LLM 视觉能力描述图片内容，生成可检索文本。
/// 支持 PNG, JPG, JPEG, GIF, WebP, SVG 格式。
/// </summary>
public class ImageContentExtractor : IFileExtractorService
{
    private static readonly HashSet<string> SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg"];

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml"
    };

    private const string VisionSystemPrompt =
        "You are a document analysis assistant. Describe images for use in a searchable knowledge base.";

    private const string VisionUserPrompt =
        "Describe this image in detail for use as searchable text in a knowledge base.";

    private readonly IChatClientFactory? _chatClientFactory;
    private readonly ILogger<ImageContentExtractor> _logger;

    public ImageContentExtractor(
        IChatClientFactory? chatClientFactory = null,
        ILogger<ImageContentExtractor>? logger = null)
    {
        _chatClientFactory = chatClientFactory;
        _logger = logger ?? NullLogger<ImageContentExtractor>.Instance;
    }

    /// <inheritdoc />
    public bool Supports(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext.ToLowerInvariant());
    }

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        Check.NotNull(content);
        Check.NotNullOrWhiteSpace(fileName);

        if (_chatClientFactory == null)
        {
            _logger.LogWarning("No IChatClientFactory available, returning fallback for image: {FileName}", fileName);
            return BuildFallback(fileName);
        }

        try
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var mimeType = MimeTypes.GetValueOrDefault(ext, "application/octet-stream");

            // 读取图片数据
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var imageBytes = ms.ToArray();

            if (imageBytes.Length == 0)
            {
                return BuildFallback(fileName);
            }

            // 构建包含图片的多模态消息
            var chatClient = _chatClientFactory.GetChatClient();
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, VisionSystemPrompt),
                new(ChatRole.User, [
                    new DataContent(imageBytes, mimeType),
                    new TextContent(VisionUserPrompt)
                ])
            };

            var chatOptions = new ChatOptions
            {
                MaxOutputTokens = 500,
                Temperature = 0.2f
            };

            var response = await chatClient.GetResponseAsync(messages, chatOptions, ct);
            var description = response.Text?.Trim();

            if (string.IsNullOrWhiteSpace(description))
            {
                _logger.LogWarning("Vision model returned empty response for image: {FileName}", fileName);
                return BuildFallback(fileName);
            }

            return $"[Image: {Path.GetFileName(fileName)}]\n{description}";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vision extraction failed for image: {FileName}, using fallback", fileName);
            return BuildFallback(fileName);
        }
    }

    private static string BuildFallback(string fileName) =>
        $"[Image: {Path.GetFileName(fileName)}]";
}
