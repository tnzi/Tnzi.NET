using UglyToad.PdfPig;

namespace Tnzi.Finance.Ai.Services;

/// <summary>
/// Default AI-backed receipt extractor: vision for images, PdfPig text for PDFs, both landing on the
/// same strongly-typed structured output.
/// </summary>
/// <remarks>
/// Framework default for Finance's optional <see cref="IReceiptExtractor"/> contract, registered by
/// <c>FinanceAiModule</c> via <c>TryAddScoped</c>; a consumer may register its own extractor to
/// override. image/* → <see cref="ChatMessage"/> + <see cref="DataContent"/> (vision);
/// application/pdf → PdfPig text. Both paths flow through
/// <see cref="IStructuredOutputService.GetStructuredOutputAsync{T}(IEnumerable{ChatMessage}, StructuredOutputOptions?, CancellationToken)"/>.
/// Files over <see cref="FinanceAiOptions.MaxFileSizeMb"/> return 400.
/// </remarks>
public class AiReceiptExtractor : ApplicationService, IReceiptExtractor
{
    private const string SystemPrompt =
        "You are a meticulous accounts-payable clerk. Extract structured fields from a receipt or invoice. " +
        "Return only the fields you can read; leave unknown fields null. Amounts are numeric without currency symbols. " +
        "Confidence is your overall extraction confidence between 0 and 1.";

    /// <summary>存储侧「不知道是什么」时给出的内容类型。</summary>
    private const string BinaryContentType = "application/octet-stream";

    private readonly IFileStorageService _storage;
    private readonly IStructuredOutputService _structuredOutput;
    private readonly IOptionsMonitor<FinanceAiOptions> _options;

    public AiReceiptExtractor(
        IServiceProvider serviceProvider,
        IFileStorageService storage,
        IStructuredOutputService structuredOutput,
        IOptionsMonitor<FinanceAiOptions> options)
        : base(serviceProvider)
    {
        _storage = Check.NotNull(storage);
        _structuredOutput = Check.NotNull(structuredOutput);
        _options = Check.NotNull(options);
    }

    public async Task<Result<ReceiptExtractionResult>> ExtractAsync(ReceiptExtractionRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);
        var opts = _options.CurrentValue;

        var infoResult = await _storage.GetFileInfoAsync(request.FileId, cancellationToken);
        if (!infoResult.Succeeded || infoResult.Data == null)
            return Fail<ReceiptExtractionResult>(infoResult.Message ?? "The receipt file was not found.", infoResult.Code ?? 404);

        var maxBytes = (long)opts.MaxFileSizeMb * 1024 * 1024;

        // 元数据里的大小只是**便宜的提前退出**（避免为一个 2GB 文件走完整条下载）。
        // 它不是闸门：FileRecord.Size 在流长度量不出来时会记 0（见 Tnzi.Storage 2026-07-28），
        // 真正的上限由下面的有界读取执行。
        if (infoResult.Data.FileSize > maxBytes)
            return Fail<ReceiptExtractionResult>(TooLargeMessage(opts.MaxFileSizeMb), 400);

        var contentType = ResolveContentType(request, infoResult.Data);
        var isImage = contentType.StartsWith("image/", StringComparison.Ordinal);
        var isPdf = contentType.Contains("pdf", StringComparison.Ordinal);

        // ★ 分支判定放在**下载之前**：拿不动的格式不该先把整个文件读进内存再拒绝。
        if (!isImage && !isPdf)
            return Fail<ReceiptExtractionResult>($"Unsupported receipt content type '{contentType}'. Upload an image or PDF.", 400);
        if (isImage && !IsVisionAccepted(contentType, opts))
        {
            return Fail<ReceiptExtractionResult>(
                $"The vision model does not accept '{contentType}'. Save the photo as JPEG or PNG, or upload a PDF.", 400);
        }

        var streamResult = await _storage.GetAsync(request.FileId);
        if (!streamResult.Succeeded || streamResult.Data == null)
            return Fail<ReceiptExtractionResult>(streamResult.Message ?? "The receipt file could not be opened.", streamResult.Code ?? 404);

        byte[] bytes;
        await using (var stream = streamResult.Data)
        {
            var bounded = await ReadBoundedAsync(stream, maxBytes, cancellationToken);
            if (bounded == null)
                return Fail<ReceiptExtractionResult>(TooLargeMessage(opts.MaxFileSizeMb), 400);
            bytes = bounded;
        }

        var structuredOptions = new StructuredOutputOptions
        {
            Provider = opts.Provider,
            ModelId = opts.Model,
            SystemPrompt = SystemPrompt
        };
        var prompt = BuildPrompt(request.HintCurrency);

        if (isImage)
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, new List<AIContent> { new TextContent(prompt), new DataContent(bytes, contentType) })
            };
            return await _structuredOutput.GetStructuredOutputAsync<ReceiptExtractionResult>(messages, structuredOptions, cancellationToken);
        }

        string text;
        try
        {
            text = ExtractPdfText(bytes, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail<ReceiptExtractionResult>($"Failed to read the PDF: {ex.Message}", 400);
        }

        if (string.IsNullOrWhiteSpace(text))
            return Fail<ReceiptExtractionResult>("The PDF has no extractable text. Upload an image scan for vision extraction.", 400);

        var pdfMessages = new List<ChatMessage>
        {
            new(ChatRole.User, $"{prompt}\n\nReceipt text:\n{text}")
        };
        var pdfResult = await _structuredOutput.GetStructuredOutputAsync<ReceiptExtractionResult>(pdfMessages, structuredOptions, cancellationToken);
        if (pdfResult.Succeeded && pdfResult.Data != null)
            pdfResult.Data.RawText = text;
        return pdfResult;
    }

    private static string TooLargeMessage(int maxFileSizeMb)
        => $"The receipt file exceeds the {maxFileSizeMb} MB limit.";

    /// <summary>
    /// 解析内容类型：显式入参 &gt; 存储元数据 &gt; <b>按扩展名回落</b>。
    /// </summary>
    /// <remarks>
    /// ★ 回落这一步是必需的：上传时若元数据没定型（或早于 <c>FileTypeHelper</c> 收录该格式时
    /// 存下的旧记录），库里留下的是 <c>application/octet-stream</c>，而收据分支只认 <c>image/*</c>
    /// 与 pdf —— 手机拍的照片会以「不支持的内容类型 application/octet-stream」被拒，
    /// 消息里连是什么格式都看不出来。扩展名回落让它至少能说出真正的格式。
    /// </remarks>
    private static string ResolveContentType(ReceiptExtractionRequest request, FileInfoDto info)
    {
        var declared = (request.ContentType ?? info.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        if (declared.Length > 0 && declared != BinaryContentType)
            return declared;

        // 原始文件名优先：存储侧的 FileName 是系统生成的，扩展名通常在但不保证语义。
        foreach (var name in new[] { request.FileName, info.FileName })
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var fromExtension = FileTypeHelper.GetContentType(Path.GetExtension(name)).ToLowerInvariant();
            if (fromExtension != BinaryContentType)
                return fromExtension;
        }

        return declared;
    }

    /// <summary>视觉模型是否收这个内容类型（选项为空 = 不拦）。</summary>
    private static bool IsVisionAccepted(string contentType, FinanceAiOptions opts)
    {
        var accepted = opts.VisionContentTypes;
        return accepted.Length == 0
            || accepted.Any(t => string.Equals(t.Trim(), contentType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 读满 <paramref name="maxBytes"/> 即放弃（返回 <see langword="null"/>）。
    /// </summary>
    /// <remarks>
    /// ★ 这是真正的大小闸门。元数据里的 <c>Size</c> 由上传那一刻写入，可能是 0
    /// （不可 seek 的流量不出长度时 Storage 会记 0 + Warning），也可能与实际对象不一致；
    /// 拿它当唯一判据等于把「整个文件读进 byte[]」交给一个不可信的数字。
    /// </remarks>
    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        using var ms = new MemoryStream();
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            if (ms.Length + read > maxBytes)
                return null;
            ms.Write(buffer, 0, read);
        }

        return ms.ToArray();
    }

    private static string BuildPrompt(string? hintCurrency)
    {
        var currencyHint = string.IsNullOrWhiteSpace(hintCurrency)
            ? string.Empty
            : $" The likely currency is {hintCurrency.Trim().ToUpperInvariant()}.";
        return "Extract the vendor name, document date, currency, subtotal, tax amount, total, a reference/invoice number, " +
               "and any line items from this receipt." + currencyHint;
    }

    private static string ExtractPdfText(byte[] bytes, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(bytes);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = page.Text;
            if (string.IsNullOrWhiteSpace(text))
                continue;
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(text);
        }
        return sb.ToString();
    }
}
