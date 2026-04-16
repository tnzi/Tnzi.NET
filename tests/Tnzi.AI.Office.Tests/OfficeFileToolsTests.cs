using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Office;
using Tnzi.AI.Office.Creators;
using Tnzi.AI.Office.Mutators;
using Tnzi.AI.Office.Tools;

namespace Tnzi.AI.Office.Tests;

/// <summary>
/// Smoke tests for <see cref="OfficeFileTools"/> — verifies JSON parsing,
/// creator/mutator dispatch, and error handling.
/// </summary>
public class OfficeFileToolsTests
{
    private readonly OfficeFileTools _sut;

    public OfficeFileToolsTests()
    {
        IOfficeFileCreator[] creators = new IOfficeFileCreator[]
        {
            new XlsxFileCreator(),
            new DocxFileCreator(),
        };
        IOfficeFileMutator[] mutators = new IOfficeFileMutator[]
        {
            new XlsxFileMutator(),
            new DocxFileMutator(),
        };
        _sut = new OfficeFileTools(creators, mutators, NullLogger<OfficeFileTools>.Instance);
    }

    [Fact]
    public async Task CreateFileAsync_Xlsx_ReturnsBase64Content()
    {
        var sheetsJson = """[{"name":"Sheet1","headers":["Col"],"rows":[["val"]]}]""";
        var result = await _sut.CreateFileAsync("out.xlsx", "xlsx", sheetsJson);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("base64_content", json);
        Assert.DoesNotContain("\"error\"", json);
    }

    [Fact]
    public async Task CreateFileAsync_Docx_ReturnsBase64Content()
    {
        var sheetsJson = """[{"name":"Doc","headers":["A"],"rows":[["B"]]}]""";
        var result = await _sut.CreateFileAsync("out.docx", "docx", sheetsJson);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("base64_content", json);
        Assert.DoesNotContain("\"error\"", json);
    }

    [Fact]
    public async Task CreateFileAsync_UnsupportedType_ReturnsError()
    {
        var result = await _sut.CreateFileAsync("out.pdf", "pdf", "[]");
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
    }

    [Fact]
    public async Task CreateFileAsync_InvalidJson_ReturnsError()
    {
        var result = await _sut.CreateFileAsync("out.xlsx", "xlsx", "not-json");
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
    }

    [Fact]
    public async Task MutateFileAsync_Xlsx_SetCell_RoundTrip()
    {
        // Create a workbook
        var sheetsJson = """[{"name":"S1","headers":["X"],"rows":[["old"]]}]""";
        var createResult = (dynamic)await _sut.CreateFileAsync("x.xlsx", "xlsx", sheetsJson);
        var base64 = JsonSerializer.Serialize(createResult).Contains("base64_content")
            ? JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(createResult))
                .GetProperty("base64_content").GetString()!
            : throw new InvalidOperationException("Create failed");

        // Mutate: set cell A2 to "new"
        var mutationsJson = """[{"type":"set_cell","sheet":"S1","row":2,"col":1,"value":"new"}]""";
        var mutResult = await _sut.MutateFileAsync("xlsx", base64, mutationsJson);

        var json = JsonSerializer.Serialize(mutResult);
        Assert.Contains("base64_content", json);
        Assert.DoesNotContain("\"error\"", json);
    }
}
