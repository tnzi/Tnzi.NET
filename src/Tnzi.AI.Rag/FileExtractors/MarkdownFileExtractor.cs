namespace Tnzi.AI.Rag.FileExtractors;

/// <summary>
/// Markdown (.md) 文件文本提取器，直接读取 UTF-8 文本
/// </summary>
public class MarkdownFileExtractor : IFileExtractorService
{
    private static readonly HashSet<string> SupportedExtensions = [".md"];

    /// <inheritdoc />
    public bool Supports(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext.ToLowerInvariant());
    }

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }
}
