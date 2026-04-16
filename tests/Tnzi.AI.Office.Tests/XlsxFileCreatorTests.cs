using ClosedXML.Excel;
using Tnzi.AI.Office;
using Tnzi.AI.Office.Creators;

namespace Tnzi.AI.Office.Tests;

public class XlsxFileCreatorTests
{
    private readonly XlsxFileCreator _sut = new();

    [Fact]
    public void SupportedExtensions_ContainsXlsx()
    {
        Assert.Contains(".xlsx", _sut.SupportedExtensions);
    }

    [Fact]
    public async Task CreateAsync_WritesHeadersAndRows()
    {
        var spec = new OfficeFileSpec("test.xlsx", ".xlsx", new[]
        {
            new OfficeSheetSpec("Sheet1",
                new[] { "Name", "Age" },
                new[] { new object?[] { "Alice", 30 }, new object?[] { "Bob", 25 } })
        });

        var stream = await _sut.CreateAsync(spec);

        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet("Sheet1");
        Assert.Equal("Name", ws.Cell(1, 1).GetValue<string>());
        Assert.Equal("Age", ws.Cell(1, 2).GetValue<string>());
        Assert.Equal("Alice", ws.Cell(2, 1).GetValue<string>());
        Assert.Equal("30", ws.Cell(2, 2).GetValue<string>());
        Assert.Equal("Bob", ws.Cell(3, 1).GetValue<string>());
    }

    [Fact]
    public async Task CreateAsync_MultipleSheets_AllPresent()
    {
        var spec = new OfficeFileSpec("multi.xlsx", ".xlsx", new[]
        {
            new OfficeSheetSpec("Alpha", new[] { "X" }, new[] { new object?[] { "1" } }),
            new OfficeSheetSpec("Beta",  new[] { "Y" }, new[] { new object?[] { "2" } }),
        });

        var stream = await _sut.CreateAsync(spec);

        using var wb = new XLWorkbook(stream);
        Assert.NotNull(wb.Worksheet("Alpha"));
        Assert.NotNull(wb.Worksheet("Beta"));
    }

    [Fact]
    public async Task CreateAsync_EmptyRows_OnlyHeaderRow()
    {
        var spec = new OfficeFileSpec("empty.xlsx", ".xlsx", new[]
        {
            new OfficeSheetSpec("Empty", new[] { "Col1", "Col2" }, Array.Empty<IReadOnlyList<object?>>())
        });

        var stream = await _sut.CreateAsync(spec);

        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet("Empty");
        Assert.Equal("Col1", ws.Cell(1, 1).GetValue<string>());
        Assert.True(ws.Cell(2, 1).IsEmpty());
    }
}
