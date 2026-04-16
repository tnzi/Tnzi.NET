using ClosedXML.Excel;

namespace Tnzi.AI.Office.Mutators;

/// <summary>
/// Mutates .xlsx workbooks by applying <see cref="SetCellMutation"/>,
/// <see cref="AppendRowMutation"/>, and <see cref="ReplaceTextMutation"/> operations.
/// Returns a new stream; the original input stream is not modified.
/// </summary>
public sealed class XlsxFileMutator : IOfficeFileMutator
{
    public string[] SupportedExtensions => new[] { ".xlsx" };

    public Task<Stream> MutateAsync(Stream input, OfficeMutationSpec spec, CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook(input);

        foreach (var mutation in spec.Mutations)
        {
            switch (mutation)
            {
                case SetCellMutation set:
                    ApplySetCell(workbook, set);
                    break;

                case AppendRowMutation append:
                    ApplyAppendRow(workbook, append);
                    break;

                case ReplaceTextMutation replace:
                    ApplyReplaceText(workbook, replace);
                    break;
            }
        }

        var output = new MemoryStream();
        workbook.SaveAs(output);
        output.Position = 0;
        return Task.FromResult<Stream>(output);
    }

    private static void ApplySetCell(XLWorkbook workbook, SetCellMutation mutation)
    {
        var ws = workbook.Worksheet(mutation.Sheet);
        ws.Cell(mutation.Row, mutation.Column).Value = mutation.Value?.ToString() ?? "";
    }

    private static void ApplyAppendRow(XLWorkbook workbook, AppendRowMutation mutation)
    {
        var ws = workbook.Worksheet(mutation.Sheet);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var newRow = lastRow + 1;

        for (int col = 0; col < mutation.Values.Count; col++)
            ws.Cell(newRow, col + 1).Value = mutation.Values[col]?.ToString() ?? "";
    }

    private static void ApplyReplaceText(XLWorkbook workbook, ReplaceTextMutation mutation)
    {
        foreach (var ws in workbook.Worksheets)
        {
            foreach (var cell in ws.CellsUsed())
            {
                if (cell.DataType == XLDataType.Text)
                {
                    var current = cell.GetValue<string>();
                    if (current.Contains(mutation.Find))
                        cell.Value = current.Replace(mutation.Find, mutation.Replace);
                }
            }
        }
    }
}
