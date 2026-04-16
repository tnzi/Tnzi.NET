using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Tnzi.AI.Office;
using Tnzi.AI.Office.Creators;
using Tnzi.AI.Office.Mutators;

namespace Tnzi.AI.Office.Tests;

public class DocxFileMutatorTests
{
    private readonly DocxFileCreator _creator = new();
    private readonly DocxFileMutator _sut = new();

    [Fact]
    public void SupportedExtensions_ContainsDocx()
    {
        Assert.Contains(".docx", _sut.SupportedExtensions);
    }

    [Fact]
    public async Task MutateAsync_ReplaceText_UpdatesContent()
    {
        var original = await CreateDocxAsync("Hello Alice");

        var spec = new OfficeMutationSpec(new[]
        {
            new ReplaceTextMutation("Alice", "Bob")
        });

        var output = await _sut.MutateAsync(original, spec);

        var allText = ExtractText(output);
        Assert.Contains("Hello Bob", allText);
        Assert.DoesNotContain("Alice", allText);
    }

    [Fact]
    public async Task MutateAsync_ReplaceText_MultipleOccurrences_AllReplaced()
    {
        var original = await CreateDocxAsync("Alice and Alice again");

        var spec = new OfficeMutationSpec(new[]
        {
            new ReplaceTextMutation("Alice", "Carol")
        });

        var output = await _sut.MutateAsync(original, spec);

        var allText = ExtractText(output);
        Assert.DoesNotContain("Alice", allText);
        Assert.Contains("Carol", allText);
    }

    [Fact]
    public async Task MutateAsync_SetCellMutation_ThrowsNotSupported()
    {
        var original = await CreateDocxAsync("sample");
        var spec = new OfficeMutationSpec(new[] { new SetCellMutation("Sheet1", 1, 1, "v") });
        await Assert.ThrowsAsync<NotSupportedException>(() => _sut.MutateAsync(original, spec));
    }

    private async Task<Stream> CreateDocxAsync(string bodyText)
    {
        var spec = new OfficeFileSpec("test.docx", ".docx", new[]
        {
            new OfficeSheetSpec("Section",
                new[] { "Content" },
                new[] { new object?[] { bodyText } })
        });
        return await _creator.CreateAsync(spec);
    }

    private static string ExtractText(Stream stream)
    {
        stream.Position = 0;
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);
        return string.Join("", doc.MainDocumentPart!.Document.Body!.Descendants<Text>().Select(t => t.Text));
    }
}
