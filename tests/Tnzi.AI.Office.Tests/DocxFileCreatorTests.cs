using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Tnzi.AI.Office;
using Tnzi.AI.Office.Creators;

namespace Tnzi.AI.Office.Tests;

public class DocxFileCreatorTests
{
    private readonly DocxFileCreator _sut = new();

    [Fact]
    public void SupportedExtensions_ContainsDocx()
    {
        Assert.Contains(".docx", _sut.SupportedExtensions);
    }

    [Fact]
    public async Task CreateAsync_ContainsSheetTitleAndHeadersAndRows()
    {
        var spec = new OfficeFileSpec("test.docx", ".docx", new[]
        {
            new OfficeSheetSpec("Report",
                new[] { "Name", "Score" },
                new[] { new object?[] { "Alice", "95" }, new object?[] { "Bob", "87" } })
        });

        var stream = await _sut.CreateAsync(spec);

        using var doc = WordprocessingDocument.Open(stream, isEditable: false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var allText = string.Join(" ", body.Descendants<Text>().Select(t => t.Text));

        Assert.Contains("Report", allText);
        Assert.Contains("Name", allText);
        Assert.Contains("Score", allText);
        Assert.Contains("Alice", allText);
        Assert.Contains("Bob", allText);
        Assert.Contains("95", allText);
    }

    [Fact]
    public async Task CreateAsync_MultipleSheets_BothTitlesPresent()
    {
        var spec = new OfficeFileSpec("multi.docx", ".docx", new[]
        {
            new OfficeSheetSpec("Summary", new[] { "A" }, new[] { new object?[] { "1" } }),
            new OfficeSheetSpec("Details", new[] { "B" }, new[] { new object?[] { "2" } }),
        });

        var stream = await _sut.CreateAsync(spec);

        using var doc = WordprocessingDocument.Open(stream, isEditable: false);
        var allText = string.Join(" ", doc.MainDocumentPart!.Document.Body!.Descendants<Text>().Select(t => t.Text));
        Assert.Contains("Summary", allText);
        Assert.Contains("Details", allText);
    }
}
