using ClosedXML.Excel;
using Tnzi.AI.Office;
using Tnzi.AI.Office.Creators;
using Tnzi.AI.Office.Mutators;

namespace Tnzi.AI.Office.Tests;

public class XlsxFileMutatorTests
{
    private readonly XlsxFileCreator _creator = new();
    private readonly XlsxFileMutator _sut = new();

    [Fact]
    public void SupportedExtensions_ContainsXlsx()
    {
        Assert.Contains(".xlsx", _sut.SupportedExtensions);
    }

    [Fact]
    public async Task MutateAsync_SetCell_UpdatesValue()
    {
        var original = await CreateWorkbookAsync("Sheet1",
            new[] { "Name", "Age" },
            new[] { new object?[] { "Alice", "30" } });

        var spec = new OfficeMutationSpec(new[]
        {
            new SetCellMutation("Sheet1", Row: 2, Column: 1, Value: "Charlie")
        });

        var output = await _sut.MutateAsync(original, spec);

        using var wb = new XLWorkbook(output);
        Assert.Equal("Charlie", wb.Worksheet("Sheet1").Cell(2, 1).GetValue<string>());
    }

    [Fact]
    public async Task MutateAsync_AppendRow_AddsRow()
    {
        var original = await CreateWorkbookAsync("Data",
            new[] { "X" },
            new[] { new object?[] { "v1" } });

        var spec = new OfficeMutationSpec(new[]
        {
            new AppendRowMutation("Data", new object?[] { "v2" })
        });

        var output = await _sut.MutateAsync(original, spec);

        using var wb = new XLWorkbook(output);
        var ws = wb.Worksheet("Data");
        Assert.Equal("v1", ws.Cell(2, 1).GetValue<string>());
        Assert.Equal("v2", ws.Cell(3, 1).GetValue<string>());
    }

    [Fact]
    public async Task MutateAsync_ReplaceText_ReplacesInAllCells()
    {
        var original = await CreateWorkbookAsync("Sheet1",
            new[] { "Col" },
            new[] { new object?[] { "Hello Alice" } });

        var spec = new OfficeMutationSpec(new[]
        {
            new ReplaceTextMutation("Alice", "Bob")
        });

        var output = await _sut.MutateAsync(original, spec);

        using var wb = new XLWorkbook(output);
        Assert.Equal("Hello Bob", wb.Worksheet("Sheet1").Cell(2, 1).GetValue<string>());
    }

    private async Task<Stream> CreateWorkbookAsync(
        string sheetName, string[] headers, object?[][] rows)
    {
        var spec = new OfficeFileSpec("test.xlsx", ".xlsx", new[]
        {
            new OfficeSheetSpec(sheetName, headers,
                rows.Select(r => (IReadOnlyList<object?>)r.ToList()).ToList())
        });
        return await _creator.CreateAsync(spec);
    }
}
