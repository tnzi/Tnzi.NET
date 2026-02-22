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

        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico" };
        return Array.Exists(imageExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

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
