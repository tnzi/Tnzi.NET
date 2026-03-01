namespace Tnzi.AI.Rag.FileExtractors;

/// <summary>
/// PDF 文件文本提取器，使用 PdfPig 库
/// </summary>
public class PdfFileExtractor : IFileExtractorService
{
    private static readonly HashSet<string> SupportedExtensions = [".pdf"];

    /// <inheritdoc />
    public bool Supports(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext.ToLowerInvariant());
    }

    /// <inheritdoc />
    public Task<string> ExtractTextAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        using var document = PdfDocument.Open(content);
        var sb = new StringBuilder();

        foreach (Page page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();

            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }
                sb.Append(text);
            }
        }

        return Task.FromResult(sb.ToString());
    }
}
