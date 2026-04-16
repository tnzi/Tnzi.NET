using System.Data;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Tnzi.Export.Exporters;

public sealed class DocxExporter
{
    public Task<Stream> ExportAsync(DataTable table, CancellationToken ct = default)
    {
        var stream = new MemoryStream();

        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            var wordTable = BuildTable(table, ct);
            body.AppendChild(wordTable);

            mainPart.Document.Save();
        }

        stream.Position = 0;
        return Task.FromResult<Stream>(stream);
    }

    private static Table BuildTable(DataTable table, CancellationToken ct)
    {
        var wordTable = new Table();
        wordTable.AppendChild(new TableProperties(new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4 },
            new BottomBorder { Val = BorderValues.Single, Size = 4 },
            new LeftBorder { Val = BorderValues.Single, Size = 4 },
            new RightBorder { Val = BorderValues.Single, Size = 4 },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

        // Header row
        var headerRow = new TableRow();
        foreach (DataColumn col in table.Columns)
        {
            headerRow.AppendChild(new TableCell(
                new Paragraph(new Run(new RunProperties(new Bold()), new Text(col.ColumnName)))));
        }
        wordTable.AppendChild(headerRow);

        // Data rows
        foreach (DataRow row in table.Rows)
        {
            ct.ThrowIfCancellationRequested();
            var tr = new TableRow();
            foreach (DataColumn col in table.Columns)
                tr.AppendChild(new TableCell(new Paragraph(new Run(new Text(row[col]?.ToString() ?? "")))));
            wordTable.AppendChild(tr);
        }

        return wordTable;
    }
}
