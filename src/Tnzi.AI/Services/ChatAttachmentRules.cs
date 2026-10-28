namespace Tnzi.AI.Services;

/// <summary>
/// 读出并校验一条聊天消息里的附件（发给绑定了外部 CLI 运行时的 Agent 时）。
/// </summary>
/// <remarks>
/// <para>
/// 接受的只有内联的图片（PNG / JPEG / GIF / WebP）与 PDF。其余一律<b>拒绝</b>而不是丢掉：
/// 一个只把文本发出去的回合看上去和成功一模一样，模型照样作答，用户以为它看到了附件。
/// </para>
/// <para>
/// 媒体类型取自字节本身（文件头），不取客户端声明的值：provider 收到声明与内容不符的图片会整轮报错，
/// 而浏览器给出的类型常常就是扩展名猜的。认不出来的内容按不支持拒绝。
/// </para>
/// <para>
/// 门面在写任何东西之前用它做第一道检查（拒绝时线程里什么都不留），调度器入队时再检查一次（直接调调度器的调用方没有门面）。
/// </para>
/// </remarks>
public static class ChatAttachmentRules
{
    /// <summary>接受的图片类型。</summary>
    public static IReadOnlyList<string> ImageMediaTypes { get; } = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    /// <summary>接受的文档类型。</summary>
    public const string PdfMediaType = "application/pdf";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// 从内容块里取出附件（文本块跳过）。没有附件返回空列表；有任何一个不合规就整体拒绝。
    /// </summary>
    public static Result<IReadOnlyList<ChatAttachmentPayload>> Read(
        IReadOnlyList<ContentPartDto>? parts, ChatAttachmentOptions limits)
    {
        Check.NotNull(limits);

        if (parts is not { Count: > 0 })
        {
            return Result<IReadOnlyList<ChatAttachmentPayload>>.Success([]);
        }

        var attachments = new List<ChatAttachmentPayload>();
        foreach (var part in parts)
        {
            Result<ChatAttachmentPayload>? read = part switch
            {
                TextContentPartDto => null,
                ImageContentPartDto image => ReadImage(image, limits),
                DocumentContentPartDto document => ReadInline(document.Base64Data, document.FileName, limits),
                FileContentPartDto => Refuse(
                    "File references cannot be sent to this agent: attach images and PDF documents inline instead.",
                    415, ErrorCodes.UnsupportedMediaType),
                _ => Refuse($"Content of type '{part.GetType().Name}' cannot be sent to this agent.", 415, ErrorCodes.UnsupportedMediaType)
            };

            if (read is null)
            {
                continue;
            }

            if (!read.Succeeded)
            {
                return Result<IReadOnlyList<ChatAttachmentPayload>>.Failure(read.Message!, read.Code ?? 400, read.ErrorCode);
            }

            attachments.Add(read.Data!);
        }

        if (attachments.Count > limits.MaxCount)
        {
            return Result<IReadOnlyList<ChatAttachmentPayload>>.Failure(
                $"A message can carry at most {limits.MaxCount} attachments.", 400, ErrorCodes.TooManyAttachments);
        }

        return Result<IReadOnlyList<ChatAttachmentPayload>>.Success(attachments);
    }

    /// <summary>附件的短描述（文件名或类型），用在给 CLI 的提示与日志里。</summary>
    public static string Describe(ChatAttachmentPayload attachment)
        => string.IsNullOrWhiteSpace(attachment.FileName) ? attachment.MediaType : attachment.FileName;

    private static Result<ChatAttachmentPayload> ReadImage(ImageContentPartDto image, ChatAttachmentOptions limits)
    {
        if (!string.IsNullOrWhiteSpace(image.Base64Data))
        {
            return ReadInline(image.Base64Data, image.FileName, limits);
        }

        if (!string.IsNullOrWhiteSpace(image.Url)
            && image.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return ReadInline(image.Url, image.FileName, limits);
        }

        // 外部 CLI 拿不到一个只有地址的图片：服务端替它去取会是一个任由客户端指定目标的出站请求，
        // 而把地址原样交给 CLI 又依赖它所在宿主的网络。
        return Refuse("Images can only be sent to this agent inline (base64), not by URL.", 415, ErrorCodes.UnsupportedMediaType);
    }

    private static Result<ChatAttachmentPayload> ReadInline(string data, string? fileName, ChatAttachmentOptions limits)
    {
        var base64 = StripDataUri(data);

        // 解码前按长度先挡一次：base64 每 4 个字符 3 个字节，超出上限的不必解出来再拒绝。
        if ((long)base64.Length / 4 * 3 > limits.MaxBytes + 3)
        {
            return TooLarge(fileName, limits);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return Refuse("An attachment is not valid base64 data.", 400, ErrorCodes.InvalidContent);
        }

        if (bytes.Length == 0)
        {
            return Refuse("An attachment is empty.", 400, ErrorCodes.InvalidContent);
        }

        if (bytes.LongLength > limits.MaxBytes)
        {
            return TooLarge(fileName, limits);
        }

        var mediaType = DetectMediaType(bytes);
        if (mediaType is null)
        {
            return Refuse(
                $"{Name(fileName)} is not a supported attachment: send PNG, JPEG, GIF or WebP images, or PDF documents.",
                415, ErrorCodes.UnsupportedMediaType);
        }

        return Result<ChatAttachmentPayload>.Success(new ChatAttachmentPayload(mediaType, Clean(fileName), bytes));
    }

    /// <summary>
    /// 按文件头认类型：PNG / JPEG / GIF / WebP 图片与 PDF，其余返回 null。
    /// 外部 CLI 的工具返回的图片也用它认（只存认得出的图片类型）。
    /// </summary>
    public static string? DetectMediaType(ReadOnlySpan<byte> bytes)
    {
        // 不能写成 u8 字面量："\x89" 是字符 U+0089，UTF-8 编码后是两个字节
        if (bytes.StartsWith(PngSignature))
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        // PDF 头允许前面有少量字节（规范允许头出现在前 1024 字节内）
        var head = bytes[..Math.Min(bytes.Length, 1024)];
        return head.IndexOf("%PDF-"u8) >= 0 ? PdfMediaType : null;
    }

    private static string StripDataUri(string data)
    {
        var trimmed = data.Trim();
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var comma = trimmed.IndexOf(',');
        return comma < 0 ? string.Empty : trimmed[(comma + 1)..];
    }

    private static string? Clean(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        // 只留文件名本身：客户端给的值会被原样显示，也会进给 CLI 的说明文字
        var name = Path.GetFileName(fileName.Replace('\\', '/').Trim());
        return name.Length > 200 ? name[..200] : name;
    }

    private static string Name(string? fileName)
        => Clean(fileName) is { } name ? $"\"{name}\"" : "An attachment";

    private static Result<ChatAttachmentPayload> TooLarge(string? fileName, ChatAttachmentOptions limits)
        => Refuse(
            $"{Name(fileName)} is larger than {Math.Max(1, limits.MaxBytes / (1024 * 1024))} MB and cannot be sent.",
            413, ErrorCodes.AttachmentTooLarge);

    private static Result<ChatAttachmentPayload> Refuse(string message, int status, string errorCode)
        => Result<ChatAttachmentPayload>.Failure(message, status, errorCode);
}
