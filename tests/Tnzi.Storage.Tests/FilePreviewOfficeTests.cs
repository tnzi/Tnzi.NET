using Tnzi.Documents;

namespace Tnzi.Storage.Tests;

/// <summary>
/// Office 文档预览：能力完全取决于可选包 <c>Tnzi.Documents</c> 是否加载。
/// </summary>
/// <remarks>
/// 这组用例守两件事：
/// <list type="number">
/// <item>没加载转换器时行为**逐字不变**（这是纯增量改动的前提）；</item>
/// <item>加载后 <c>CanPreview</c> / <c>GetPreviewType</c> / <c>GeneratePreviewAsync</c> 三处判定**一致** ——
///   控制器拿 <c>CanPreview</c> 当闸门，它说 false 就直接 400，生成方法根本不会被调到；
///   而 <c>GetPreviewType</c> 决定响应的 Content-Type。三者只要有一处不同步，
///   症状就是「说能预览，点开是 octet-stream 下载」或「明明能转却报不支持」。</item>
/// </list>
/// </remarks>
public class FilePreviewOfficeTests
{
    private const string DocxPath = "uploads/contract.docx";

    private static FileRecord DocxRecord() => new()
    {
        Id = Guid.NewGuid(),
        FileName = "contract.docx",
        OriginalName = "contract.docx",
        Extension = ".docx",
        Path = DocxPath,
    };

    private static FilePreviewService CreateSut(IDocumentConverter? converter, Mock<IFileStorage>? storage = null)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        return new FilePreviewService(
            (storage ?? new Mock<IFileStorage>()).Object,
            serviceProvider.Object,
            converter);
    }

    // ---------- 没有转换器：行为必须与改动前逐字一致 ----------

    [Fact]
    public void WithoutConverter_OfficeIsNotPreviewable()
    {
        var sut = CreateSut(converter: null);

        Assert.False(sut.CanPreview(DocxRecord()));
    }

    [Fact]
    public void WithoutConverter_PreviewTypeStaysOffice()
    {
        var sut = CreateSut(converter: null);

        Assert.Equal("office", sut.GetPreviewType(DocxRecord()));
    }

    [Fact]
    public async Task WithoutConverter_GeneratingPreviewThrowsNotSupported()
    {
        var sut = CreateSut(converter: null);

        await Assert.ThrowsAsync<NotSupportedException>(() => sut.GeneratePreviewAsync(DocxRecord()));
    }

    // ---------- 有转换器：三处判定必须同步 ----------

    [Fact]
    public void WithConverter_OfficeBecomesPreviewable()
    {
        var sut = CreateSut(new StubConverter());

        Assert.True(sut.CanPreview(DocxRecord()));
    }

    [Fact]
    public void WithConverter_PreviewTypeIsPdf_SoTheResponseGetsTheRightContentType()
    {
        // 控制器按这个字符串查 Content-Type 表，表里没有 "office" 分支 ——
        // 报 "office" 就会以 application/octet-stream 发出去，浏览器直接下载而不是预览。
        var sut = CreateSut(new StubConverter());

        Assert.Equal("pdf", sut.GetPreviewType(DocxRecord()));
    }

    [Fact]
    public async Task WithConverter_PreviewStreamIsTheConvertedPdf()
    {
        var pdf = "%PDF-1.7 converted"u8.ToArray();
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.DownloadAsync(DocxPath))
            .ReturnsAsync(() => new MemoryStream("original docx bytes"u8.ToArray()));

        var sut = CreateSut(new StubConverter(pdf), storage);

        await using var preview = await sut.GeneratePreviewAsync(DocxRecord());
        using var buffer = new MemoryStream();
        await preview.CopyToAsync(buffer);

        Assert.Equal(pdf, buffer.ToArray());
    }

    [Fact]
    public async Task WithConverter_TheOriginalIsDownloadedAndHandedToTheConverter()
    {
        var original = "original docx bytes"u8.ToArray();
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.DownloadAsync(DocxPath)).ReturnsAsync(() => new MemoryStream(original));
        var converter = new StubConverter();

        var sut = CreateSut(converter, storage);
        await using var _ = await sut.GeneratePreviewAsync(DocxRecord());

        Assert.Equal(original, converter.LastSource);
        storage.Verify(s => s.DownloadAsync(DocxPath), Times.Once);
    }

    // ---------- DI 解析：整套「可选注入」方案的地基 ----------

    [Fact]
    public void TheContainerResolvesIt_WithoutTheOptionalConverterRegistered()
    {
        // ★ 上面那些用例全是手工 new 出来的，**绕过了容器**，证明不了这一条：
        //   没加载 Tnzi.Documents 时 `IDocumentConverter` 根本没注册，
        //   内置容器必须能靠构造函数默认值把这个服务造出来。造不出来的话
        //   单测照样全绿，而任何一个没加载可选包的应用一请求预览就崩。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<IFileStorage>());
        services.AddScoped<IFilePreviewService, FilePreviewService>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<IFilePreviewService>();

        Assert.False(sut.CanPreview(DocxRecord()));
        Assert.Equal("office", sut.GetPreviewType(DocxRecord()));
    }

    [Fact]
    public void TheContainerInjectsTheConverter_WhenTheOptionalPackageIsLoaded()
    {
        // 反向：注册了实现时它确实被注进去了（而不是默认值把注册盖掉）。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<IFileStorage>());
        services.AddSingleton<IDocumentConverter>(new StubConverter());
        services.AddScoped<IFilePreviewService, FilePreviewService>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<IFilePreviewService>();

        Assert.True(sut.CanPreview(DocxRecord()));
        Assert.Equal("pdf", sut.GetPreviewType(DocxRecord()));
    }

    // ---------- 递给转换器的那个字符串 ----------

    [Fact]
    public void TheConverterReceivesSomethingItCanParseAnExtensionFrom()
    {
        // 转换器的入参名叫 fileName，实际只取扩展名（`Path.GetExtension`）。守的是
        // 「别递一个解析不出扩展名的字段」—— 递 Path、递 Id、或递一个不带点的扩展名
        // （`Path.GetExtension("docx")` 是空串）都会让每一份 Office 文档被静默判成不可转换。
        var converter = new StubConverter();
        var sut = CreateSut(converter);

        sut.CanPreview(DocxRecord());

        Assert.NotNull(converter.LastProbedFileName);
        Assert.Equal(".docx", System.IO.Path.GetExtension(converter.LastProbedFileName!));
    }

    [Fact]
    public void WhenTheConverterRejectsTheExtension_OfficeStaysUnpreviewable()
    {
        // 加载了 Tnzi.Documents 不等于什么都能转（转换器有自己的支持列表）。
        var sut = CreateSut(new StubConverter { Accepts = false });

        Assert.False(sut.CanPreview(DocxRecord()));
        Assert.Equal("office", sut.GetPreviewType(DocxRecord()));
    }

    // ---------- 运行环境不齐备（加载了包，但宿主没装 LibreOffice） ----------

    [Fact]
    public void WhenTheConverterIsUnavailable_OfficeStaysUnpreviewable()
    {
        // ★ 这是**默认情形**而不是边缘情况：Tnzi.Signing 是 Tnzi.Documents 的主要消费者，
        //   它只用盖章与定位，根本不需要 LibreOffice。若只问 CanConvert（它只查格式白名单），
        //   这种部署下 CanPreview 会答 true，用户点开预览才在转换那步炸成 500。
        var sut = CreateSut(new StubConverter { Available = false });

        Assert.False(sut.CanPreview(DocxRecord()));
        Assert.Equal("office", sut.GetPreviewType(DocxRecord()));
    }

    [Fact]
    public async Task WhenTheConverterIsUnavailable_GeneratingPreviewThrowsNotSupported_NotAConversionError()
    {
        // 生成路径必须与 CanPreview 同步：给出「不支持」而不是让转换异常冒到控制器变 500。
        var sut = CreateSut(new StubConverter { Available = false });

        await Assert.ThrowsAsync<NotSupportedException>(() => sut.GeneratePreviewAsync(DocxRecord()));
    }

    /// <summary>
    /// 记录被问到了什么的假转换器。不用 Moq 是因为这几个用例的断言对象正是「传进来的参数」。
    /// </summary>
    private sealed class StubConverter : IDocumentConverter
    {
        private readonly byte[] _pdf;

        public StubConverter(byte[]? pdf = null) => _pdf = pdf ?? "%PDF-1.7"u8.ToArray();

        public bool Accepts { get; init; } = true;

        /// <summary>运行环境是否齐备（真实实现里 = soffice 找不找得到）。</summary>
        public bool Available { get; init; } = true;

        public bool IsAvailable => Available;

        public string? LastProbedFileName { get; private set; }

        public byte[]? LastSource { get; private set; }

        public bool CanConvert(string fileName)
        {
            LastProbedFileName = fileName;
            return Accepts;
        }

        public Task<byte[]> ConvertToPdfAsync(byte[] source, string sourceFileName, CancellationToken ct = default)
        {
            LastSource = source;
            LastProbedFileName = sourceFileName;
            return Task.FromResult(_pdf);
        }
    }
}
