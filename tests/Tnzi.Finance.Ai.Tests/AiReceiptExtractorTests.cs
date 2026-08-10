namespace Tnzi.Finance.Ai.Tests;

/// <summary>
/// 默认收据提取器的两道门：内容类型与大小。
/// </summary>
/// <remarks>
/// <para>
/// 本模块此前<b>零测试</b>。它只有两百多行，但它是「拍一张照就能录一笔费用」这条链上
/// 唯一的守门人，而它的两道门各有一个只在生产才现形的缺陷：
/// </para>
/// <list type="bullet">
/// <item><b>内容类型</b>：分支只认 <c>image/*</c> 与 pdf，而 <c>.heic</c>（iPhone 相机默认）
/// 与 <c>.tiff</c>（扫描仪默认）此前在 <c>FileTypeHelper</c> 里根本不存在 → 存储元数据记的是
/// <c>application/octet-stream</c> → 收据被拒，消息里连是什么格式都看不出来。
/// <b>而这正是手机拍照与扫描件的主流格式。</b></item>
/// <item><b>大小</b>：只读存储元数据里的 <c>FileSize</c>，而那个数字在流长度量不出来时会被
/// 记成 0（Storage 2026-07-28 的既定回退）—— 于是闸门形同虚设，整个文件照样被读进
/// <c>byte[]</c>。</item>
/// </list>
/// <para>
/// 每条测试锁的都是上面某一条判断，且都带一条<b>对照</b>用例（该放行的仍放行）——
/// 只写「不该通过」的断言看不出自己在验一个从没被调用过的分支。
/// </para>
/// </remarks>
public class AiReceiptExtractorTests
{
    private static readonly Guid FileId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ── 内容类型：按扩展名回落 ────────────────────────────────────────────────

    /// <summary>
    /// 元数据是 octet-stream 但文件名认得出来 → 按扩展名回落，走视觉路径。
    /// </summary>
    /// <remarks>
    /// 这是 <c>.jpg</c> 因为任何原因没定型时的补救路径。没有它，一张普通照片会以
    /// 「不支持的内容类型 application/octet-stream」被拒。
    /// </remarks>
    [Theory]
    [InlineData("receipt.jpg", "image/jpeg")]
    [InlineData("receipt.JPEG", "image/jpeg")]
    [InlineData("scan.png", "image/png")]
    public async Task Binary_content_type_falls_back_to_the_file_extension(string fileName, string expected)
    {
        var fixture = new Fixture { StoredContentType = "application/octet-stream", FileName = fileName };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId, FileName = fileName });

        result.Succeeded.ShouldBeTrue(result.Message);
        // 送给模型的内容类型必须是解析出来的那个，否则 provider 收到 octet-stream 照样报错
        fixture.CapturedImageContentType.ShouldBe(expected);
    }

    /// <summary>
    /// 显式声明的内容类型优先于扩展名（调用方比存储元数据知道得多）。
    /// </summary>
    [Fact]
    public async Task Explicit_content_type_wins_over_the_extension()
    {
        var fixture = new Fixture { StoredContentType = "application/octet-stream", FileName = "receipt.bin" };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest
        {
            FileId = FileId,
            FileName = "receipt.bin",
            ContentType = "image/png"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        fixture.CapturedImageContentType.ShouldBe("image/png");
    }

    // ── 内容类型：视觉模型收不收 ──────────────────────────────────────────────

    /// <summary>
    /// HEIC / TIFF 现在能被认成图片，但视觉模型不收 —— 必须给出<b>可操作</b>的拒绝消息。
    /// </summary>
    /// <remarks>
    /// 直接把字节送过去只会换回一句供应商侧的报错，最终对用户显示成
    /// 「提取失败，详见服务端日志」—— 他无从知道该怎么做。这条断言要求消息里
    /// <b>点出格式名</b>并<b>说出下一步</b>，且模型一次都不该被调用。
    /// </remarks>
    [Theory]
    [InlineData("photo.heic", "image/heic")]
    [InlineData("photo.HEIF", "image/heif")]
    [InlineData("scan.tiff", "image/tiff")]
    [InlineData("scan.tif", "image/tiff")]
    public async Task Image_the_vision_model_does_not_accept_is_rejected_with_the_format_named(
        string fileName, string resolved)
    {
        var fixture = new Fixture { StoredContentType = "application/octet-stream", FileName = fileName };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId, FileName = fileName });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldContain(resolved);
        result.Message.ShouldContain("JPEG");
        fixture.ModelCalls.ShouldBe(0);
    }

    /// <summary>
    /// 清空 <c>VisionContentTypes</c> = 不拦：接了自己 provider 或过一道转码的部署照常能用。
    /// </summary>
    /// <remarks>
    /// 这条是上一条的对照。没有它，「视觉格式白名单」就成了写死在框架里的天花板，
    /// 而消费方能不能用某个格式取决于他们的 provider，不取决于我们。
    /// </remarks>
    [Fact]
    public async Task Empty_vision_content_types_disables_the_gate()
    {
        var fixture = new Fixture
        {
            StoredContentType = "image/heic",
            FileName = "photo.heic",
            VisionContentTypes = []
        };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId, FileName = "photo.heic" });

        result.Succeeded.ShouldBeTrue(result.Message);
        fixture.CapturedImageContentType.ShouldBe("image/heic");
    }

    /// <summary>既不是图片也不是 PDF → 400，且<b>不下载</b>。</summary>
    [Fact]
    public async Task Unsupported_content_type_is_rejected_before_the_file_is_downloaded()
    {
        var fixture = new Fixture { StoredContentType = "application/msword", FileName = "notes.doc" };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId, FileName = "notes.doc" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        fixture.StreamOpened.ShouldBeFalse();
        fixture.ModelCalls.ShouldBe(0);
    }

    // ── 大小闸门 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 元数据说超限 → 立刻 400，连流都不打开（便宜的提前退出）。
    /// </summary>
    [Fact]
    public async Task Metadata_over_the_limit_short_circuits_without_downloading()
    {
        var fixture = new Fixture
        {
            StoredContentType = "image/jpeg",
            FileName = "big.jpg",
            MaxFileSizeMb = 1,
            ReportedSize = 5L * 1024 * 1024
        };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId, FileName = "big.jpg" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldContain("1 MB");
        fixture.StreamOpened.ShouldBeFalse();
    }

    /// <summary>
    /// ★ 元数据说 0 字节而实际内容超限 → 仍被拦下，且模型一次都不调。
    /// </summary>
    /// <remarks>
    /// <c>FileRecord.Size</c> 在流长度量不出来时会被记成 0（Storage 的既定回退），
    /// 拿它当唯一判据等于把「整个文件读进内存」交给一个不可信的数字。
    /// 这条测试是「真正的闸门在有界读取那一步」的可执行证明。
    /// </remarks>
    [Fact]
    public async Task Content_over_the_limit_is_rejected_even_when_the_metadata_says_zero()
    {
        var fixture = new Fixture
        {
            StoredContentType = "image/jpeg",
            FileName = "lying.jpg",
            MaxFileSizeMb = 1,
            ReportedSize = 0,
            ContentBytes = new byte[(1 * 1024 * 1024) + 1]
        };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId, FileName = "lying.jpg" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldContain("1 MB");
        fixture.ModelCalls.ShouldBe(0);
    }

    /// <summary>
    /// 恰好等于上限的内容仍放行 —— 有界读取不得把边界值一起拦掉。
    /// </summary>
    [Fact]
    public async Task Content_exactly_at_the_limit_is_accepted()
    {
        var fixture = new Fixture
        {
            StoredContentType = "image/jpeg",
            FileName = "exact.jpg",
            MaxFileSizeMb = 1,
            ReportedSize = 0,
            ContentBytes = new byte[1 * 1024 * 1024]
        };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId, FileName = "exact.jpg" });

        result.Succeeded.ShouldBeTrue(result.Message);
        fixture.ModelCalls.ShouldBe(1);
    }

    // ── PDF 路径 ──────────────────────────────────────────────────────────────

    /// <summary>读不动的 PDF 落成 400，而不是让 PdfPig 的异常冒到最外层变成 500。</summary>
    [Fact]
    public async Task Unreadable_pdf_becomes_a_400_not_an_unhandled_exception()
    {
        var fixture = new Fixture
        {
            StoredContentType = "application/pdf",
            FileName = "broken.pdf",
            ContentBytes = "this is not a pdf"u8.ToArray()
        };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId, FileName = "broken.pdf" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldContain("PDF");
        fixture.ModelCalls.ShouldBe(0);
    }

    // ── 未找到 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Missing_file_propagates_the_storage_failure()
    {
        var fixture = new Fixture { FileMissing = true };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        fixture.StreamOpened.ShouldBeFalse();
    }

    // ── 夹具 ──────────────────────────────────────────────────────────────────

    private sealed class Fixture
    {
        public string StoredContentType { get; init; } = "image/jpeg";
        public string FileName { get; init; } = "receipt.jpg";
        public long ReportedSize { get; init; } = 1024;
        public byte[] ContentBytes { get; init; } = [1, 2, 3, 4];
        public int MaxFileSizeMb { get; init; } = 20;
        public string[] VisionContentTypes { get; init; } = ["image/jpeg", "image/png", "image/gif", "image/webp"];
        public bool FileMissing { get; init; }

        /// <summary>视觉路径实际递给模型的内容类型（null = 没走视觉路径）。</summary>
        public string? CapturedImageContentType { get; private set; }

        /// <summary>模型被调用的次数（拒绝路径必须为 0）。</summary>
        public int ModelCalls { get; private set; }

        /// <summary>文件流是否被打开过（提前退出路径必须为 false）。</summary>
        public bool StreamOpened { get; private set; }

        public AiReceiptExtractor Build()
        {
            var storage = new Mock<IFileStorageService>();
            storage.Setup(s => s.GetFileInfoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => FileMissing
                    ? Result.Failure<FileInfoDto>("The file was not found.", 404)
                    : Result.Success(new FileInfoDto
                    {
                        FileId = FileId,
                        FileName = FileName,
                        FileSize = ReportedSize,
                        ContentType = StoredContentType,
                    }));
            storage.Setup(s => s.GetAsync(It.IsAny<Guid>()))
                .ReturnsAsync(() =>
                {
                    StreamOpened = true;
                    return Result.Success<Stream>(new MemoryStream(ContentBytes, writable: false));
                });

            var structured = new Mock<IStructuredOutputService>();
            structured.Setup(s => s.GetStructuredOutputAsync<ReceiptExtractionResult>(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<StructuredOutputOptions>(), It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<ChatMessage> messages, StructuredOutputOptions? _, CancellationToken __) =>
                {
                    ModelCalls++;
                    CapturedImageContentType = messages
                        .SelectMany(m => m.Contents)
                        .OfType<DataContent>()
                        .Select(d => d.MediaType)
                        .FirstOrDefault();
                    return Task.FromResult(Result.Success(new ReceiptExtractionResult { Confidence = 0.9m }));
                });

            var options = new Mock<IOptionsMonitor<FinanceAiOptions>>();
            options.SetupGet(o => o.CurrentValue).Returns(new FinanceAiOptions
            {
                MaxFileSizeMb = MaxFileSizeMb,
                VisionContentTypes = VisionContentTypes,
            });

            var provider = new ServiceCollection().BuildServiceProvider();
            return new AiReceiptExtractor(provider, storage.Object, structured.Object, options.Object);
        }
    }
}
