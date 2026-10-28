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

    /// <summary>
    /// 回显的进程输出截到上限：截断点落在代理对中间时整个字符一起舍掉，日志与异常消息里不留孤立的高代理项。
    /// </summary>
    [Fact]
    public void CapturedOutput_CutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        var limit = LibreOfficeDocumentConverter.CapturedOutputLimit;
        // 第 limit 个码元是 emoji 的前半个
        var output = new string('a', limit - 1) + "\U0001F600tail";

        LibreOfficeDocumentConverter.TruncateCapturedOutput(output).ShouldBe(new string('a', limit - 1) + "...");
        LibreOfficeDocumentConverter.TruncateCapturedOutput("  ").ShouldBe(string.Empty);
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

    /// <summary>
    /// 排队等闸门有上界：等满一次转换的超时仍轮不到，就以可重试的转换失败返回，而不是无限期挂着。
    /// </summary>
    /// <remarks>
    /// 闸门是进程级单槽，每一次 Office 预览都要过它。一份慢文档或刻意构造的文档能占满整个超时，
    /// 其后的预览全排在它后面；此前排队这一步不设上界（调用方又多半不传取消令牌），
    /// 用户早就离开页面的请求还挂在队里，轮到时再完整跑一遍 soffice。
    /// 这里先把闸门占住，配置路径指向一个存在的文件（闸门之后才会执行它，本用例走不到那里）。
    /// </remarks>
    [Fact]
    public async Task ConvertToPdfAsync_QueueStaysBusyPastTheTimeout_FailsRetryablyInsteadOfWaitingForever()
    {
        var existingFile = typeof(LibreOfficeDocumentConverterTests).Assembly.Location;
        var converter = CreateConverter(options =>
        {
            options.LibreOfficePath = existingFile;
            options.ConversionTimeoutSeconds = 1;
        });

        await LibreOfficeDocumentConverter.ConversionGate.WaitAsync();
        try
        {
            // 兜底：若排队不设上界，30 秒后以取消收场（断言失败），而不是让整个测试进程挂住。
            using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var exception = await Should.ThrowAsync<DocumentConversionException>(
                () => converter.ConvertToPdfAsync(RtfDocument(), "contract.rtf", safety.Token));

            exception.IsRetryable.ShouldBeTrue();
            exception.Message.ShouldContain("busy");
            safety.IsCancellationRequested.ShouldBeFalse();
        }
        finally
        {
            LibreOfficeDocumentConverter.ConversionGate.Release();
            LibreOfficeLocator.ResetCache();
        }
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
