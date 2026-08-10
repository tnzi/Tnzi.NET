namespace Tnzi.Storage.Helpers;

/// <summary>
/// 文件类型辅助工具类
/// 提供文件类型判断和内容类型获取等功能
/// </summary>
public static class FileTypeHelper
{
    /// <summary>
    /// 判断是否为图片文件
    /// </summary>
    /// <param name="extension">文件扩展名（包含点号，如 .jpg）</param>
    /// <returns>是否为图片</returns>
    public static bool IsImage(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        // ★ .heic/.heif 是 iOS 相机的默认格式，.tif/.tiff 是扫描仪的默认格式 —— 它们不在这张
        // 表里的后果不是「判不出是图片」而是 GetContentType 回落 application/octet-stream，
        // 于是任何按 image/* 分支的下游（预览、缩略图、收据识别）都当它是二进制附件拒掉。
        var imageExtensions = new[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico",
            ".heic", ".heif", ".avif", ".tif", ".tiff"
        };
        return Array.Exists(imageExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 本机图片解码器（ImageSharp 3.1）读不了的图片格式。
    /// </summary>
    /// <remarks>
    /// <c>.svg</c> 是矢量格式，ImageSharp 从来不支持；<c>.heic</c>/<c>.heif</c>/<c>.avif</c> 需要
    /// 额外编解码器（框架刻意锁在 ImageSharp 3.1.x 免授权线上，见 <c>Tnzi.Imaging.csproj</c>）。
    /// <para>
    /// <c>.ico</c> <b>刻意不在此列</b>：它在 3.1 上能不能解码没有实测过，而它本来就走缩略图路径 ——
    /// 列进来会改变一个与本表无关的既有行为。
    /// </para>
    /// </remarks>
    private static readonly string[] NonDecodableImageExtensions = [".svg", ".heic", ".heif", ".avif"];

    /// <summary>
    /// 判断这个图片格式是否<b>解得开</b>（可用于生成缩略图 / 二次编码）。
    /// </summary>
    /// <remarks>
    /// ★ 与 <see cref="IsImage"/> <b>正交</b>，调用方要区分两个问题：
    /// 「这是不是一张图」（决定 Content-Type、决定要不要按图片呈现）与
    /// 「我们的解码器读不读得了」（决定能不能生成缩略图）。
    /// <para>
    /// 混用的后果是**可预期的失败被当成异常记录**：`.heic` 是 iOS 相机默认格式，
    /// 每张上传的照片都会让缩略图解码抛一次、在日志里留一条 ERROR，
    /// 而那既不是错误也没有人能处理。`.svg` 从来就是这样（在本方法出现之前就是）。
    /// </para>
    /// 这与 <c>Tnzi.Documents</c> 的 <c>CanConvert</c>（格式白名单）vs <c>IsAvailable</c>
    /// （宿主装了 LibreOffice 吗）是同一条教训：**两个正交的问题，调用方必须都问**。
    /// </remarks>
    /// <param name="extension">文件扩展名（包含点号）</param>
    public static bool IsThumbnailable(string extension)
        => IsImage(extension)
           && !Array.Exists(NonDecodableImageExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 判断是否为 PDF 文件。
    /// </summary>
    /// <param name="extension">文件扩展名</param>
    /// <returns>是否为 PDF</returns>
    public static bool IsPdf(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断是否为视频文件
    /// </summary>
    /// <param name="extension">文件扩展名</param>
    /// <returns>是否为视频</returns>
    public static bool IsVideo(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        var videoExtensions = new[] { ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mkv" };
        return Array.Exists(videoExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判断是否为音频文件
    /// </summary>
    /// <param name="extension">文件扩展名</param>
    /// <returns>是否为音频</returns>
    public static bool IsAudio(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        var audioExtensions = new[] { ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a" };
        return Array.Exists(audioExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判断是否为文本文件
    /// </summary>
    /// <param name="extension">文件扩展名</param>
    /// <returns>是否为文本</returns>
    public static bool IsText(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        var textExtensions = new[] { ".txt", ".md", ".json", ".xml", ".html", ".css", ".js", ".cs", ".java", ".py", ".log" };
        return Array.Exists(textExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判断是否为 Office 文档。
    /// </summary>
    /// <param name="extension">文件扩展名</param>
    /// <returns>是否为 Office 文档</returns>
    public static bool IsOffice(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        var officeExtensions = new[] { ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp" };
        return Array.Exists(officeExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 根据文件扩展名获取内容类型（MIME 类型）
    /// </summary>
    /// <param name="extension">文件扩展名（包含点号，如 .jpg）</param>
    /// <returns>内容类型</returns>
    public static string GetContentType(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return "application/octet-stream";

        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            ".avif" => "image/avif",
            ".tif" or ".tiff" => "image/tiff",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            _ => "application/octet-stream"
        };
    }
}
