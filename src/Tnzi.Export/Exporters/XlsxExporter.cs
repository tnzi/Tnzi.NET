using System.Data;
using ClosedXML.Excel;

namespace Tnzi.Export.Exporters;

public sealed class XlsxExporter
{
    public Task<Stream> ExportAsync(DataTable table, CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Export");

        // Header row
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = table.Columns[i].ColumnName;
            cell.Style.Font.Bold = true;
        }

        // Data rows
        for (int r = 0; r < table.Rows.Count; r++)
        {
            ct.ThrowIfCancellationRequested();
            for (int c = 0; c < table.Columns.Count; c++)
                worksheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(table.Rows[r][c]);
        }

        worksheet.Columns().AdjustToContents();

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return Task.FromResult<Stream>(stream);
    }
}
