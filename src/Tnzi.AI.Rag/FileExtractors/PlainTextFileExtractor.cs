namespace Tnzi.AI.Rag.FileExtractors;

/// <summary>
/// 纯文本文件提取器，支持 .txt, .csv, .log, .json, .xml, .yaml, .yml
/// </summary>
public class PlainTextFileExtractor : IFileExtractorService
{
    private static readonly HashSet<string> SupportedExtensions =
        [".txt", ".csv", ".log", ".json", ".xml", ".yaml", ".yml"];

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
