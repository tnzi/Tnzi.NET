namespace Tnzi.AI.Tools;

/// <summary>
/// 图片查看工具 - 提供图片文件读取和验证功能，供 AI Agent 查看图片内容。
/// </summary>
public static class ViewImageTool
{
    /// <summary>支持的图片扩展名</summary>
    public static readonly HashSet<string> ValidExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    /// <summary>扩展名到 MIME 类型的映射</summary>
    public static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif"
    };

    /// <summary>属性键：已查看的图片列表</summary>
    public const string ViewedImagesKey = "ViewedImages";

    /// <summary>属性键：图片是否已注入标记</summary>
    public const string ViewedImagesInjectedKey = "ViewedImagesInjected";

    /// <summary>
    /// 检查文件扩展名是否为支持的图片格式
    /// </summary>
    public static bool IsValidImageExtension(string extension)
    {
        return ValidExtensions.Contains(extension);
    }

    /// <summary>
    /// 获取扩展名对应的 MIME 类型
    /// </summary>
    public static string GetMimeType(string extension)
    {
        return MimeTypes.TryGetValue(extension, out var mimeType)
            ? mimeType
            : "application/octet-stream";
    }

    /// <summary>最大图片文件大小（20MB）</summary>
    private const long MaxImageFileSize = 20 * 1024 * 1024;

    /// <summary>
    /// 读取图片文件并返回 Base64 编码字符串
    /// </summary>
    /// <returns>Base64 字符串，文件不存在或超过大小限制时返回 null</returns>
    public static async Task<string?> ReadImageAsBase64Async(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return null;

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxImageFileSize)
            return null;

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// 创建查看图片的 AIFunction，供 AI Agent 调用
    /// </summary>
    public static AIFunction CreateViewImageFunction(AiMiddlewareContext context)
    {
        Check.NotNull(context);

        return AIFunctionFactory.Create(
            async (string filePath, CancellationToken cancellationToken) =>
            {
                // 验证扩展名
                var extension = Path.GetExtension(filePath);
                if (string.IsNullOrEmpty(extension) || !IsValidImageExtension(extension))
                {
                    return $"Unsupported image format '{extension}'. Supported formats: {string.Join(", ", ValidExtensions)}";
                }

                if (!File.Exists(filePath))
                {
                    return $"Image file not found: {filePath}";
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MaxImageFileSize)
                {
                    return $"Image file too large ({fileInfo.Length / 1024 / 1024}MB). Maximum allowed: {MaxImageFileSize / 1024 / 1024}MB";
                }

                var base64 = await ReadImageAsBase64Async(filePath, cancellationToken);
                if (base64 == null)
                {
                    return $"Failed to read image file: {filePath}";
                }

                var mimeType = GetMimeType(extension);

                // 存入上下文属性
                if (!context.Properties.TryGetValue(ViewedImagesKey, out var existing)
                    || existing is not List<(string MimeType, string Base64)> images)
                {
                    images = [];
                    context.Properties[ViewedImagesKey] = images;
                }

                images.Add((mimeType, base64));

                return $"Image '{Path.GetFileName(filePath)}' loaded successfully ({mimeType}, {base64.Length} chars base64).";
            },
            new AIFunctionFactoryOptions
            {
                Name = "view_image",
                Description = "View an image file. Provide the full file path to load the image for visual analysis."
            });
    }
}
