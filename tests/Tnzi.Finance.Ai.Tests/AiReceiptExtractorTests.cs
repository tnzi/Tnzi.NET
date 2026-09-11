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
        result.Message!.ShouldContain(resolved);
        result.Message!.ShouldContain("JPEG");
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
        result.Message!.ShouldContain("1 MB");
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
        result.Message!.ShouldContain("1 MB");
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
        result.Message!.ShouldContain("PDF");
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

    // ── PDF：成功路径与提示词长度闸门 ─────────────────────────────────────────

    /// <summary>
    /// PDF 走文本路径：正文进提示词，原文回填 <c>RawText</c>。
    /// </summary>
    /// <remarks>
    /// 此前十条测试全是拒绝路径 —— 一个从未被走通的成功路径，与「这条路径根本不通」
    /// 在测试结果上完全一样。
    /// </remarks>
    [Fact]
    public async Task Pdf_text_reaches_the_prompt_and_comes_back_as_raw_text()
    {
        var fixture = new Fixture
        {
            StoredContentType = "application/pdf",
            FileName = "invoice.pdf",
            ContentBytes = PdfWithText("ACME SUPPLIES TOTAL 123.45")
        };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId, FileName = "invoice.pdf" });

        result.Succeeded.ShouldBeTrue(result.Message);
        fixture.ModelCalls.ShouldBe(1);
        fixture.CapturedText!.ShouldContain("ACME SUPPLIES TOTAL 123.45");
        result.Data!.RawText!.ShouldContain("ACME SUPPLIES TOTAL 123.45");
    }

    /// <summary>
    /// ★ 正文超过上限 → 截断并注明，而不是把上百万字符原样拼进提示词。
    /// </summary>
    /// <remarks>
    /// 字节大小有两道闸门，文本长度此前一道都没有。注明是必需的：模型看不见被截掉的部分，
    /// 不告诉它就会拿倒数第二个数字凑一个看起来合理的总计。
    /// </remarks>
    [Fact]
    public async Task Pdf_text_longer_than_the_limit_is_truncated_and_says_so()
    {
        const int limit = 1000;
        var fixture = new Fixture
        {
            StoredContentType = "application/pdf",
            FileName = "long.pdf",
            MaxPdfTextChars = limit,
            ContentBytes = PdfWithText(string.Join(" ", Enumerable.Repeat("LOREMIPSUM", 400)))
        };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId, FileName = "long.pdf" });

        result.Succeeded.ShouldBeTrue(result.Message);
        var sent = fixture.CapturedText!;
        // 提示词 = 指令 + 正文；正文部分必须已被截到上限（留出注明那一段的余量）
        sent.Length.ShouldBeLessThan(limit + 500);
        sent.ShouldContain("Text truncated");
        // 人核对时看到的与模型看到的是同一份东西
        result.Data!.RawText!.ShouldContain("Text truncated");
    }

    /// <summary>对照：不超限时不加任何注明，正文逐字送出。</summary>
    [Fact]
    public async Task Pdf_text_within_the_limit_is_sent_verbatim()
    {
        var fixture = new Fixture
        {
            StoredContentType = "application/pdf",
            FileName = "short.pdf",
            MaxPdfTextChars = 1000,
            ContentBytes = PdfWithText("SHORT RECEIPT 9.99")
        };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest { FileId = FileId, FileName = "short.pdf" });

        result.Succeeded.ShouldBeTrue(result.Message);
        fixture.CapturedText!.ShouldNotContain("Text truncated");
    }

    /// <summary>
    /// 只有已知的 PDF 内容类型才走 PdfPig。
    /// </summary>
    /// <remarks>
    /// 判据原先是 <c>Contains("pdf")</c>，于是 <c>application/vnd.pdf-viewer</c> 这类东西
    /// 也会被送进 PdfPig，报错落在读文件那一步、消息说的是「读不了这份 PDF」——
    /// 而用户上传的根本不是 PDF。
    /// </remarks>
    [Fact]
    public async Task A_content_type_that_merely_mentions_pdf_is_not_treated_as_one()
    {
        var fixture = new Fixture { StoredContentType = "application/vnd.pdf-viewer", FileName = "thing.bin" };
        var extractor = fixture.Build();

        var result = await extractor.ExtractAsync(new ReceiptExtractionRequest
        {
            FileId = FileId,
            FileName = "thing.bin",
            ContentType = "application/vnd.pdf-viewer"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Unsupported");
        fixture.StreamOpened.ShouldBeFalse("拿不动的格式不该先把文件读进内存");
    }

    /// <summary>用 PdfPig 自己造一份带文字的最小 PDF（标准 14 号字体，无外部字体依赖）。</summary>
    private static byte[] PdfWithText(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 780;
        foreach (var chunk in Chunks(text, 90))
        {
            page.AddText(chunk, 10, new PdfPoint(30, y), font);
            y -= 12;
            if (y < 30)
            {
                page = builder.AddPage(PageSize.A4);
                y = 780;
            }
        }
        return builder.Build();
    }

    private static IEnumerable<string> Chunks(string value, int size)
    {
        for (var i = 0; i < value.Length; i += size)
            yield return value.Substring(i, Math.Min(size, value.Length - i));
    }

    private sealed class Fixture
    {
        public string StoredContentType { get; init; } = "image/jpeg";
        public string FileName { get; init; } = "receipt.jpg";
        public long ReportedSize { get; init; } = 1024;
        public byte[] ContentBytes { get; init; } = [1, 2, 3, 4];
        public int MaxFileSizeMb { get; init; } = 20;
        public int MaxPdfTextChars { get; init; } = 20000;
        public string[] VisionContentTypes { get; init; } = ["image/jpeg", "image/png", "image/gif", "image/webp"];
        public bool FileMissing { get; init; }

        /// <summary>视觉路径实际递给模型的内容类型（null = 没走视觉路径）。</summary>
        public string? CapturedImageContentType { get; private set; }

        /// <summary>实际递给模型的文本（PDF 路径）。</summary>
        public string? CapturedText { get; private set; }

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
                    var list = messages.ToList();
                    CapturedImageContentType = list
                        .SelectMany(m => m.Contents)
                        .OfType<DataContent>()
                        .Select(d => d.MediaType)
                        .FirstOrDefault();
                    CapturedText = string.Concat(list
                        .SelectMany(m => m.Contents)
                        .OfType<TextContent>()
                        .Select(t => t.Text));
                    return Task.FromResult(Result.Success(new ReceiptExtractionResult { Confidence = 0.9m }));
                });

            var options = new Mock<IOptionsMonitor<FinanceAiOptions>>();
            options.SetupGet(o => o.CurrentValue).Returns(new FinanceAiOptions
            {
                MaxFileSizeMb = MaxFileSizeMb,
                MaxPdfTextChars = MaxPdfTextChars,
                VisionContentTypes = VisionContentTypes,
            });

            var provider = new ServiceCollection().BuildServiceProvider();
            return new AiReceiptExtractor(provider, storage.Object, structured.Object, options.Object);
        }
    }
}
