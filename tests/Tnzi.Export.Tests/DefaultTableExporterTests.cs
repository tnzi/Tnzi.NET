using System.Data;
using Tnzi.Export;
using Tnzi.Export.Models;

namespace Tnzi.Export.Tests;

public class DefaultTableExporterTests
{
    private readonly DefaultTableExporter _sut = new();

    private static DataTable BuildTable()
    {
        var t = new DataTable();
        t.Columns.Add("Name", typeof(string));
        t.Columns.Add("Age", typeof(int));
        t.Rows.Add("Alice", 30);
        t.Rows.Add("Bob", 25);
        t.Rows.Add("Carol", 35);
        return t;
    }

    [Fact]
    public async Task Csv_ContainsHeaderAndRows()
    {
        var stream = await _sut.ExportAsync(BuildTable(), ExportFormat.Csv);
        using var reader = new System.IO.StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        Assert.Contains("Name,Age", text);
        Assert.Contains("Alice,30", text);
    }

    [Fact]
    public async Task Xlsx_HasZipMagicNumber()
    {
        var stream = await _sut.ExportAsync(BuildTable(), ExportFormat.Xlsx);
        var bytes = new byte[4];
        _ = await stream.ReadAsync(bytes);
        Assert.Equal(0x50, bytes[0]);
        Assert.Equal(0x4B, bytes[1]);
    }

    [Fact]
    public async Task Docx_HasZipMagicNumber()
    {
        var stream = await _sut.ExportAsync(BuildTable(), ExportFormat.Docx);
        var bytes = new byte[4];
        _ = await stream.ReadAsync(bytes);
        Assert.Equal(0x50, bytes[0]);
        Assert.Equal(0x4B, bytes[1]);
    }

    [Fact]
    public async Task Pdf_HasPdfMagicNumber()
    {
        var stream = await _sut.ExportAsync(BuildTable(), ExportFormat.Pdf);
        var bytes = new byte[4];
        _ = await stream.ReadAsync(bytes);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }
}
