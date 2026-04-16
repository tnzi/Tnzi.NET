using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Tnzi.AI.Office.Creators;

/// <summary>
/// Creates .docx documents from an <see cref="OfficeFileSpec"/>.
/// Each <see cref="OfficeSheetSpec"/> renders as a titled table in the document body.
/// </summary>
public sealed class DocxFileCreator : IOfficeFileCreator
{
    public string[] SupportedExtensions => new[] { ".docx" };

    public Task<Stream> CreateAsync(OfficeFileSpec spec, CancellationToken ct = default)
    {
        var stream = new MemoryStream();

        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            foreach (var sheet in spec.Sheets)
            {
                // Sheet title as a bold paragraph
                body.AppendChild(new Paragraph(
                    new ParagraphProperties(new ParagraphMarkRunProperties(new Bold())),
                    new Run(new RunProperties(new Bold()), new Text(sheet.Name))));

                var table = BuildTable(sheet);
                body.AppendChild(table);

                // Spacer between sheets
                body.AppendChild(new Paragraph());
            }

            mainPart.Document.Save();
        }

        stream.Position = 0;
        return Task.FromResult<Stream>(stream);
    }

    private static Table BuildTable(OfficeSheetSpec sheet)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4 },
            new BottomBorder { Val = BorderValues.Single, Size = 4 },
            new LeftBorder { Val = BorderValues.Single, Size = 4 },
            new RightBorder { Val = BorderValues.Single, Size = 4 },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

        // Header row
        var headerRow = new TableRow();
        foreach (var h in sheet.Headers)
        {
            headerRow.AppendChild(new TableCell(
                new Paragraph(new Run(new RunProperties(new Bold()), new Text(h)))));
        }
        table.AppendChild(headerRow);

        // Data rows
        foreach (var row in sheet.Rows)
        {
            var tr = new TableRow();
            foreach (var cell in row)
                tr.AppendChild(new TableCell(new Paragraph(new Run(new Text(cell?.ToString() ?? "")))));
            table.AppendChild(tr);
        }

        return table;
    }
}
