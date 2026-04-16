using System.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tnzi.Export.Exporters;

public sealed class PdfTableExporter
{
    public Task<Stream> ExportAsync(DataTable table, CancellationToken ct = default)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var stream = new MemoryStream();
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.Content().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        for (int i = 0; i < table.Columns.Count; i++)
                            cols.RelativeColumn();
                    });
                    t.Header(h =>
                    {
                        foreach (DataColumn col in table.Columns)
                            h.Cell().Element(CellStyle).Text(col.ColumnName).Bold();
                    });
                    foreach (DataRow row in table.Rows)
                    {
                        ct.ThrowIfCancellationRequested();
                        for (int i = 0; i < table.Columns.Count; i++)
                            t.Cell().Element(CellStyle).Text(row[i]?.ToString() ?? "");
                    }
                    static IContainer CellStyle(IContainer c) => c.Border(0.5f).Padding(3);
                });
            });
        }).GeneratePdf(stream);
        stream.Position = 0;
        return Task.FromResult<Stream>(stream);
    }
}
