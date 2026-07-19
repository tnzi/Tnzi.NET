using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

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
        if (infoResult.Data.FileSize > maxBytes)
            return Fail<ReceiptExtractionResult>($"The receipt file exceeds the {opts.MaxFileSizeMb} MB limit.", 400);

        var contentType = (request.ContentType ?? infoResult.Data.ContentType ?? string.Empty).Trim().ToLowerInvariant();

        var streamResult = await _storage.GetAsync(request.FileId);
        if (!streamResult.Succeeded || streamResult.Data == null)
            return Fail<ReceiptExtractionResult>(streamResult.Message ?? "The receipt file could not be opened.", streamResult.Code ?? 404);

        byte[] bytes;
        await using (var stream = streamResult.Data)
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            bytes = ms.ToArray();
        }

        var structuredOptions = new StructuredOutputOptions
        {
            Provider = opts.Provider,
            ModelId = opts.Model,
            SystemPrompt = SystemPrompt
        };
        var prompt = BuildPrompt(request.HintCurrency);

        if (contentType.StartsWith("image/", StringComparison.Ordinal))
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, new List<AIContent> { new TextContent(prompt), new DataContent(bytes, contentType) })
            };
            return await _structuredOutput.GetStructuredOutputAsync<ReceiptExtractionResult>(messages, structuredOptions, cancellationToken);
        }

        if (contentType.Contains("pdf", StringComparison.Ordinal))
        {
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

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, $"{prompt}\n\nReceipt text:\n{text}")
            };
            var result = await _structuredOutput.GetStructuredOutputAsync<ReceiptExtractionResult>(messages, structuredOptions, cancellationToken);
            if (result.Succeeded && result.Data != null)
                result.Data.RawText = text;
            return result;
        }

        return Fail<ReceiptExtractionResult>($"Unsupported receipt content type '{contentType}'. Upload an image or PDF.", 400);
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
