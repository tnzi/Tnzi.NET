

using ClosedXML.Excel;

namespace Tnzi.AI.Office.Creators;

/// <summary>
/// Creates .xlsx workbooks from an <see cref="OfficeFileSpec"/>.
/// Each <see cref="OfficeSheetSpec"/> maps to one worksheet.
/// </summary>
public sealed class XlsxFileCreator : IOfficeFileCreator
{
    public string[] SupportedExtensions => new[] { ".xlsx" };

    public Task<Stream> CreateAsync(OfficeFileSpec spec, CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook();

        foreach (var sheet in spec.Sheets)
        {
            var ws = workbook.Worksheets.Add(sheet.Name);

            for (int col = 0; col < sheet.Headers.Count; col++)
                ws.Cell(1, col + 1).Value = sheet.Headers[col];

            for (int row = 0; row < sheet.Rows.Count; row++)
            {
                var rowData = sheet.Rows[row];
                for (int col = 0; col < rowData.Count; col++)
                    ws.Cell(row + 2, col + 1).Value = rowData[col]?.ToString() ?? "";
            }

            ws.Columns().AdjustToContents();
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return Task.FromResult<Stream>(stream);
    }
}
