using System.Text;
using Xunit.Abstractions;

namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="LibreOfficeDocumentConverter"/> 的可转判定与失败路径。
/// </summary>
/// <remarks>
/// 真实转换只在本机确实装了 LibreOffice 时才跑（见
/// <see cref="ConvertToPdfAsync_WithRealLibreOffice_ProducesAReadablePdf"/>）：
/// 转换器依赖外部进程，把它设成硬性前置会让 CI 无谓变红。
/// </remarks>
public class LibreOfficeDocumentConverterTests
{
    private readonly ITestOutputHelper _output;

    public LibreOfficeDocumentConverterTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("contract.docx")]
    [InlineData("contract.DOC")]
    [InlineData("terms.odt")]
    [InlineData("notes.rtf")]
    [InlineData("sheet.xlsx")]
    [InlineData("deck.pptx")]
    public void CanConvert_KnownOfficeFormats_ReturnsTrue(string fileName)
    {
        CreateConverter().CanConvert(fileName).ShouldBeTrue();
    }

    [Theory]
    [InlineData("contract.pdf")]   // PDF 无需转换，调用方直接透传
    [InlineData("photo.png")]
    [InlineData("archive.zip")]
    [InlineData("no-extension")]
    [InlineData("")]
    public void CanConvert_NonConvertibleInputs_ReturnsFalse(string fileName)
    {
        CreateConverter().CanConvert(fileName).ShouldBeFalse();
    }

    [Fact]
    public async Task ConvertToPdfAsync_ConfiguredPathDoesNotExist_ThrowsWithConfigurationGuidance()
    {
        // 配置了路径就**不回退**自动探测：即便本机装了 LibreOffice，配错也必须报错。
        var missing = Path.Combine(Path.GetTempPath(), "tnzi-no-such-libreoffice", "soffice.exe");
        var converter = CreateConverter(options => options.LibreOfficePath = missing);

        var exception = await Should.ThrowAsync<DocumentConversionException>(
            () => converter.ConvertToPdfAsync(RtfDocument(), "contract.rtf"));

        exception.Message.ShouldContain("Documents:LibreOfficePath");
        exception.Message.ShouldContain(missing);
    }

    [Fact]
    public async Task ConvertToPdfAsync_UnsupportedExtension_ThrowsAndListsTheSupportedOnes()
    {
        var converter = CreateConverter();

        var exception = await Should.ThrowAsync<DocumentConversionException>(
            () => converter.ConvertToPdfAsync(RtfDocument(), "photo.png"));

        exception.Message.ShouldContain(".png");
        exception.Message.ShouldContain(".docx");
    }

    [Fact]
    public async Task ConvertToPdfAsync_AlreadyAPdf_IsRejectedSoCallersPassItThrough()
    {
        var converter = CreateConverter();

        await Should.ThrowAsync<DocumentConversionException>(
            () => converter.ConvertToPdfAsync(RtfDocument(), "contract.pdf"));
    }

    [Fact]
    public async Task ConvertToPdfAsync_EmptySource_Throws()
    {
        var converter = CreateConverter();

        await Should.ThrowAsync<DocumentConversionException>(
            () => converter.ConvertToPdfAsync([], "contract.rtf"));
    }

    [Fact]
    public async Task ConvertToPdfAsync_WithRealLibreOffice_ProducesAReadablePdf()
    {
        LibreOfficeLocator.ResetCache();
        var executable = LibreOfficeLocator.Resolve(null);
        if (executable == null)
        {
            _output.WriteLine("Skipped: LibreOffice is not installed on this machine.");
            return;
        }

        _output.WriteLine($"Converting with LibreOffice at '{executable}'.");
        var converter = CreateConverter();

        var pdf = await converter.ConvertToPdfAsync(RtfDocument(), "contract.rtf");

        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        new PdfPigPdfInspector().GetInfo(pdf).PageCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    private static LibreOfficeDocumentConverter CreateConverter(Action<DocumentsOptions>? configure = null)
    {
        var options = new DocumentsOptions();
        configure?.Invoke(options);

        return new LibreOfficeDocumentConverter(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<LibreOfficeDocumentConverter>.Instance);
    }

    /// <summary>最小的合法 RTF 文档：足够 LibreOffice 转出一页 PDF，又不需要造 zip 容器。</summary>
    private static byte[] RtfDocument()
        => Encoding.ASCII.GetBytes(@"{\rtf1\ansi\deff0 {\fonttbl{\f0 Helvetica;}}\f0\fs24 Tnzi document primitives.\par}");
}
