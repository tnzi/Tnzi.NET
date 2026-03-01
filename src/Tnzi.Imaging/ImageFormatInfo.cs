namespace Tnzi.Imaging;

/// <summary>
/// Image format detection result containing format name, MIME type, and file extension.
/// </summary>
/// <param name="FormatName">Format name (e.g., "PNG", "JPEG", "WebP")</param>
/// <param name="MimeType">MIME type (e.g., "image/png")</param>
/// <param name="FileExtension">File extension including dot (e.g., ".png")</param>
public record ImageFormatInfo(string FormatName, string MimeType, string FileExtension);
