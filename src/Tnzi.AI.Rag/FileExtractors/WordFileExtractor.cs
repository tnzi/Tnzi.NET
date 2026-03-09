namespace Tnzi.AI.Rag.FileExtractors;

/// <summary>
/// Word (.docx) 文件文本提取器，使用 OpenXml 库
/// </summary>
public class WordFileExtractor : IFileExtractorService
{
    private static readonly HashSet<string> SupportedExtensions = [".docx"];

    /// <inheritdoc />
    public bool Supports(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext.ToLowerInvariant());
    }

    /// <inheritdoc />
    public Task<string> ExtractTextAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        using var document = WordprocessingDocument.Open(content, false);
        var mainPart = document.MainDocumentPart;
        var body = mainPart?.Document?.Body;
        if (body is null)
        {
            return Task.FromResult(string.Empty);
        }

        var sb = new StringBuilder();
        foreach (var paragraph in body.Elements<Paragraph>())
        {
            ct.ThrowIfCancellationRequested();

            var text = paragraph.InnerText;
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
