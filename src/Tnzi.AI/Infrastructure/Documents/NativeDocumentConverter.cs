using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig;

namespace Tnzi.AI.Infrastructure.Documents;

/// <summary>
/// 基于 .NET 原生库的文档转换器（PdfPig + OpenXml）
/// </summary>
public class NativeDocumentConverter : IDocumentConverter
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
    };

    private static readonly HashSet<string> LegacyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".xls", ".ppt"
    };

    private readonly ILogger<NativeDocumentConverter> _logger;

    public NativeDocumentConverter(ILogger<NativeDocumentConverter> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public bool CanConvert(string extension)
    {
        return SupportedExtensions.Contains(extension);
    }

    /// <inheritdoc />
    public async Task<string?> ConvertToMarkdownAsync(string filePath, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File not found: {FilePath}", filePath);
            return null;
        }

        var extension = Path.GetExtension(filePath);
        if (!CanConvert(extension))
        {
            _logger.LogWarning("Unsupported file extension: {Extension}", extension);
            return null;
        }

        if (LegacyExtensions.Contains(extension))
        {
            return $"[Legacy format {extension} is not supported by native converter. Use CliDocumentConverter for .doc/.xls/.ppt files.]";
        }

        try
        {
            return extension.ToLowerInvariant() switch
            {
                ".pdf" => await ConvertPdfAsync(filePath, ct),
                ".docx" => await ConvertDocxAsync(filePath, ct),
                ".xlsx" => await ConvertXlsxAsync(filePath, ct),
                ".pptx" => await ConvertPptxAsync(filePath, ct),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert document: {FilePath}", filePath);
            return null;
        }
    }

    private Task<string> ConvertPdfAsync(string filePath, CancellationToken ct)
    {
        var sb = new StringBuilder();

        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();

            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            sb.AppendLine(page.Text);
        }

        return Task.FromResult(sb.ToString());
    }

    private Task<string> ConvertDocxAsync(string filePath, CancellationToken ct)
    {
        var sb = new StringBuilder();

        using var document = WordprocessingDocument.Open(filePath, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body == null)
        {
            return Task.FromResult(string.Empty);
        }

        foreach (var paragraph in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            ct.ThrowIfCancellationRequested();

            var text = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
                sb.AppendLine();
            }
        }

        return Task.FromResult(sb.ToString());
    }

    private Task<string> ConvertXlsxAsync(string filePath, CancellationToken ct)
    {
        var sb = new StringBuilder();

        using var document = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart == null)
        {
            return Task.FromResult(string.Empty);
        }

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? [];

        foreach (var sheet in sheets)
        {
            ct.ThrowIfCancellationRequested();

            var sheetId = sheet.Id?.Value;
            if (sheetId == null) continue;

            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheetId);
            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
            if (sheetData == null) continue;

            var sheetName = sheet.Name?.Value ?? "Sheet";
            sb.AppendLine($"## {sheetName}");
            sb.AppendLine();

            var isFirstRow = true;
            foreach (var row in sheetData.Elements<Row>())
            {
                ct.ThrowIfCancellationRequested();

                var cells = row.Elements<Cell>().ToList();
                var values = cells.Select(c => GetCellValue(c, sharedStrings)).ToList();

                sb.AppendLine("| " + string.Join(" | ", values) + " |");

                if (isFirstRow)
                {
                    sb.AppendLine("| " + string.Join(" | ", values.Select(_ => "---")) + " |");
                    isFirstRow = false;
                }
            }

            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.Text ?? string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString
            && sharedStrings != null
            && int.TryParse(value, out var index))
        {
            return sharedStrings.ElementAt(index).InnerText;
        }

        return value;
    }

    private Task<string> ConvertPptxAsync(string filePath, CancellationToken ct)
    {
        var sb = new StringBuilder();

        using var document = PresentationDocument.Open(filePath, false);
        var presentationPart = document.PresentationPart;
        if (presentationPart?.Presentation?.SlideIdList == null)
        {
            return Task.FromResult(string.Empty);
        }

        var slideIndex = 1;
        foreach (var slideId in presentationPart.Presentation.SlideIdList
                     .Elements<DocumentFormat.OpenXml.Presentation.SlideId>())
        {
            ct.ThrowIfCancellationRequested();

            var relationshipId = slideId.RelationshipId?.Value;
            if (relationshipId == null) continue;

            var slidePart = (SlidePart)presentationPart.GetPartById(relationshipId);
            var slideText = slidePart.Slide?.InnerText ?? string.Empty;

            sb.AppendLine($"### Slide {slideIndex}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(slideText))
            {
                sb.AppendLine(slideText);
                sb.AppendLine();
            }

            slideIndex++;
        }

        return Task.FromResult(sb.ToString());
    }
}
