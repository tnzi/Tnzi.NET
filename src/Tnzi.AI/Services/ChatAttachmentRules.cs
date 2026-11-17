namespace Tnzi.AI.Services;

/// <summary>
/// 读出并校验一条聊天消息里的附件（发给绑定了外部 CLI 运行时的 Agent 时）。
/// </summary>
/// <remarks>
/// <para>
/// 随提示交给模型的只有内联的图片（PNG / JPEG / GIF / WebP）与 PDF。其余的内容默认一律<b>拒绝</b>而不是丢掉：
/// 一个只把文本发出去的回合看上去和成功一模一样，模型照样作答，用户以为它看到了附件。
/// </para>
/// <para>
/// 调用方说这一轮收文件时（<c>acceptFiles</c>，由 <see cref="ICliFileAttachmentPolicy"/> 决定），其余的内容读成<b>文件</b>
/// （<see cref="ChatAttachmentPayload.IsFile"/>）：随消息存档、由 agent 用自己的工具打开，不作为内容块交给模型。
/// 文件的媒体类型取客户端声明的（字节认不出来，也就无从核对）；声明的是图片或 PDF 类型而字节不是，按
/// <c>application/octet-stream</c> 记，免得它被当成图片或 PDF 交给模型。文件一定有文件名（客户端没给时是 <c>attachment</c>）：
/// agent 靠它找到文件，下载时也按它存。
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

    /// <summary>文件没有可用的声明类型时记的媒体类型。</summary>
    public const string OctetStreamMediaType = "application/octet-stream";

    /// <summary>客户端没给文件名的文件记的名字。</summary>
    public const string DefaultFileName = "attachment";

    /// <summary>媒体类型的最大长度（与附件表的列宽一致）。</summary>
    private const int MaxMediaTypeLength = 100;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// 从内容块里取出附件（文本块跳过）。没有附件返回空列表；有任何一个不合规就整体拒绝。
    /// </summary>
    /// <param name="parts">内容块。</param>
    /// <param name="limits">上限。</param>
    /// <param name="acceptFiles">
    /// false（缺省）= 不是图片或 PDF 的内容以 415 拒绝；true = 读成文件（<see cref="ChatAttachmentPayload.IsFile"/>），
    /// 大小按 <see cref="ChatAttachmentOptions.MaxFileBytes"/>。要不要收由调用方再判（<see cref="RefuseFile"/>）。
    /// </param>
    public static Result<IReadOnlyList<ChatAttachmentPayload>> Read(
        IReadOnlyList<ContentPartDto>? parts, ChatAttachmentOptions limits, bool acceptFiles = false)
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
                ImageContentPartDto image => ReadImage(image, limits, acceptFiles),
                DocumentContentPartDto document => ReadInline(document.Base64Data, document.FileName, document.MediaType, limits, acceptFiles),
                FileContentPartDto => Refuse(
                    "File references cannot be sent to this agent: attach the file's content inline instead.",
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

    /// <summary>
    /// 媒体类型是不是随提示交给模型的那几种（PNG / JPEG / GIF / WebP 图片、PDF）。
    /// 存下的上传附件按它分：是的随提示投递，其余是 agent 用工具打开的文件。
    /// </summary>
    public static bool IsInlineMediaType(string? mediaType)
        => mediaType is not null && (mediaType == PdfMediaType || ImageMediaTypes.Contains(mediaType));

    /// <summary>
    /// 这一轮不收文件时，对一个文件附件的拒绝（415 <c>AI_UNSUPPORTED_MEDIA_TYPE</c>）：与不收文件时 <see cref="Read"/> 给的同一句话。
    /// </summary>
    public static Result<IReadOnlyList<ChatAttachmentPayload>> RefuseFile(ChatAttachmentPayload file)
    {
        Check.NotNull(file);
        return Result<IReadOnlyList<ChatAttachmentPayload>>.Failure(UnsupportedMessage(file.FileName), 415, ErrorCodes.UnsupportedMediaType);
    }

    /// <summary>
    /// 带着文件的一轮收不收它们：注册了 <see cref="ICliFileAttachmentPolicy"/> 且全部放行才收；一个都没注册时不收。
    /// </summary>
    public static async Task<bool> AcceptsFilesAsync(
        IEnumerable<ICliFileAttachmentPolicy> policies, CliFileAttachmentRequest request, CancellationToken cancellationToken)
    {
        Check.NotNull(policies);
        Check.NotNull(request);

        var asked = false;
        foreach (var policy in policies)
        {
            asked = true;
            if (!await policy.AcceptsFilesAsync(request, cancellationToken))
            {
                return false;
            }
        }

        return asked;
    }

    private static Result<ChatAttachmentPayload> ReadImage(ImageContentPartDto image, ChatAttachmentOptions limits, bool acceptFiles)
    {
        if (!string.IsNullOrWhiteSpace(image.Base64Data))
        {
            return ReadInline(image.Base64Data, image.FileName, image.MediaType, limits, acceptFiles);
        }

        if (!string.IsNullOrWhiteSpace(image.Url)
            && image.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return ReadInline(image.Url, image.FileName, image.MediaType, limits, acceptFiles);
        }

        // 外部 CLI 拿不到一个只有地址的图片：服务端替它去取会是一个任由客户端指定目标的出站请求，
        // 而把地址原样交给 CLI 又依赖它所在宿主的网络。
        return Refuse("Images can only be sent to this agent inline (base64), not by URL.", 415, ErrorCodes.UnsupportedMediaType);
    }

    private static Result<ChatAttachmentPayload> ReadInline(
        string data, string? fileName, string? declaredMediaType, ChatAttachmentOptions limits, bool acceptFiles)
    {
        var base64 = StripDataUri(data);

        // 解码前按长度先挡一次：base64 每 4 个字符 3 个字节，超出上限的不必解出来再拒绝。
        // 收文件时类型要解出来才知道，先按两个上限里大的那个挡
        var ceiling = acceptFiles ? Math.Max(limits.MaxBytes, limits.MaxFileBytes) : limits.MaxBytes;
        if ((long)base64.Length / 4 * 3 > ceiling + 3)
        {
            return TooLarge(fileName, ceiling);
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

        var mediaType = DetectMediaType(bytes);
        if (mediaType is not null || !acceptFiles)
        {
            if (bytes.LongLength > limits.MaxBytes)
            {
                return TooLarge(fileName, limits.MaxBytes);
            }

            return mediaType is null
                ? Refuse(UnsupportedMessage(fileName), 415, ErrorCodes.UnsupportedMediaType)
                : Result<ChatAttachmentPayload>.Success(new ChatAttachmentPayload(mediaType, Clean(fileName), bytes));
        }

        if (bytes.LongLength > limits.MaxFileBytes)
        {
            return TooLarge(fileName, limits.MaxFileBytes);
        }

        return Result<ChatAttachmentPayload>.Success(
            new ChatAttachmentPayload(FileMediaType(declaredMediaType), Clean(fileName) ?? DefaultFileName, bytes, IsFile: true));
    }

    /// <summary>
    /// 文件记的媒体类型：客户端声明的（只取 <c>type/subtype</c>，小写），形状不对、太长、或是图片与 PDF 的类型时
    /// （字节已经核过不是）为 <see cref="OctetStreamMediaType"/>。
    /// </summary>
    private static string FileMediaType(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return OctetStreamMediaType;
        }

        var type = declared.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (type.Length > MaxMediaTypeLength || IsInlineMediaType(type) || !IsMediaTypeToken(type))
        {
            return OctetStreamMediaType;
        }

        return type;
    }

    /// <summary><c>type/subtype</c>，两段都是 RFC 6838 的受限名字字符。</summary>
    private static bool IsMediaTypeToken(string value)
    {
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash == value.Length - 1 || value.IndexOf('/', slash + 1) >= 0)
        {
            return false;
        }

        foreach (var c in value)
        {
            var allowed = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '/' or '!' or '#' or '$' or '&' or '^' or '_' or '.' or '+' or '-';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
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

        // 只留文件名本身：客户端给的值会被原样显示，会进给 CLI 的说明文字，消费方还按它把文件放进工作目录。
        // 控制字符（换行之类）换掉：一个带换行的名字能在给 agent 的说明里另起一行写指令。"." 与 ".." 不是文件名
        var name = Path.GetFileName(fileName.Replace('\\', '/').Trim());
        name = new string(name.Select(c => char.IsControl(c) ? '_' : c).ToArray()).Trim();
        if (name.Length > 200)
        {
            name = name[..200];
        }

        return name is "" or "." or ".." ? null : name;
    }

    private static string Name(string? fileName)
        => Clean(fileName) is { } name ? $"\"{name}\"" : "An attachment";

    private static string UnsupportedMessage(string? fileName)
        => $"{Name(fileName)} is not a supported attachment: send PNG, JPEG, GIF or WebP images, or PDF documents.";

    private static Result<ChatAttachmentPayload> TooLarge(string? fileName, long maxBytes)
        => Refuse(
            $"{Name(fileName)} is larger than {Math.Max(1, maxBytes / (1024 * 1024))} MB and cannot be sent.",
            413, ErrorCodes.AttachmentTooLarge);

    private static Result<ChatAttachmentPayload> Refuse(string message, int status, string errorCode)
        => Result<ChatAttachmentPayload>.Failure(message, status, errorCode);
}
