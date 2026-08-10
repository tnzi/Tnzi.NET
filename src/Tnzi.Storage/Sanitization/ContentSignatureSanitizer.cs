namespace Tnzi.Storage.Sanitization;

/// <summary>
/// 按文件头字节核对「声称的类型」与「真实的内容」是否一致。
/// </summary>
/// <remarks>
/// <para>
/// <strong>解决的问题：</strong>扩展名和 <c>Content-Type</c> 都由上传方给出，可以随便写。
/// 把一个可执行文件或 HTML（内含脚本）命名为 <c>photo.jpg</c> 上传，
/// 下游若按扩展名决定怎么处理它，就会拿到与预期完全不同的东西。
/// </para>
/// <para>
/// <strong>这不是杀毒。</strong>它只回答「这坨字节看起来像不像它自称的类型」，
/// 一个货真价实的、带恶意宏的 <c>.docx</c> 会正常通过。真正的恶意内容检测需要
/// 另外注册一个接扫描引擎的 <see cref="IUploadSanitizer"/>。
/// </para>
/// <para>
/// <strong>默认不注册。</strong>需要时在模块里显式注册：
/// <code>
/// context.Services.AddScoped&lt;IUploadSanitizer, ContentSignatureSanitizer&gt;();
/// </code>
/// 未在签名表中的扩展名一律放行（<strong>只拒绝确知不符的</strong>），
/// 否则每加一种新格式都要改框架。
/// </para>
/// </remarks>
public sealed class ContentSignatureSanitizer : IUploadSanitizer
{
    /// <summary>
    /// 已知格式的文件头签名。一个扩展名可以有多个合法起始字节序列。
    /// </summary>
    /// <remarks>
    /// 只收录「伪装收益高」的常见类型。JPEG 只比对前两字节（第三字节各厂商不同），
    /// 这是刻意的宽松：宁可漏判也不要把用户合法的照片挡在门外。
    /// </remarks>
    private static readonly Dictionary<string, byte[][]> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        [".gif"] = ["GIF87a"u8.ToArray(), "GIF89a"u8.ToArray()],
        [".bmp"] = [[0x42, 0x4D]],
        [".webp"] = [[0x52, 0x49, 0x46, 0x46]], // RIFF 容器，WEBP 标记在偏移 8
        [".pdf"] = [[0x25, 0x50, 0x44, 0x46]],  // %PDF
        [".zip"] = [[0x50, 0x4B, 0x03, 0x04], [0x50, 0x4B, 0x05, 0x06], [0x50, 0x4B, 0x07, 0x08]],
        // OOXML 与 ODF 都是 zip 容器
        [".docx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".xlsx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".pptx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".gz"] = [[0x1F, 0x8B]],
        [".rar"] = [[0x52, 0x61, 0x72, 0x21, 0x1A, 0x07]],
        [".7z"] = [[0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]],
        [".mp4"] = [[0x66, 0x74, 0x79, 0x70]],  // 'ftyp'，在偏移 4
        [".doc"] = [[0xD0, 0xCF, 0x11, 0xE0]],
        [".xls"] = [[0xD0, 0xCF, 0x11, 0xE0]],
        [".ppt"] = [[0xD0, 0xCF, 0x11, 0xE0]],
    };

    /// <summary>
    /// 需要跳过前若干字节才能比对的格式（容器格式的标记不在文件最开头）。
    /// </summary>
    private static readonly Dictionary<string, int> Offsets = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = 4,
    };

    /// <summary>读取的文件头长度，足够覆盖表内所有签名。</summary>
    private const int HeaderLength = 16;

    /// <inheritdoc />
    /// <remarks>属于廉价拒绝，放在管线前段，免得为一个注定被拒的文件跑扫描或重编码。</remarks>
    public int Order => 10;

    /// <inheritdoc />
    public async Task<UploadSanitizationResult> SanitizeAsync(
        UploadSanitizationContext context,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        if (string.IsNullOrEmpty(context.Extension)
            || !Signatures.TryGetValue(context.Extension, out var expected))
        {
            // 未收录的扩展名放行：这个净化器只负责拒绝「确知不符」的。
            return UploadSanitizationResult.Unchanged();
        }

        if (!context.Content.CanSeek)
        {
            // 读不回开头就没法把完整内容交给存储；不可 seek 的流交由后续环节处理。
            return UploadSanitizationResult.Unchanged();
        }

        var origin = context.Content.Position;
        var buffer = new byte[HeaderLength];
        int read;
        try
        {
            read = await context.Content.ReadAtLeastAsync(
                buffer, HeaderLength, throwOnEndOfStream: false, cancellationToken);
        }
        finally
        {
            context.Content.Position = origin;
        }

        var offset = Offsets.TryGetValue(context.Extension, out var o) ? o : 0;
        var header = buffer.AsSpan(0, read);

        foreach (var signature in expected)
        {
            if (header.Length >= offset + signature.Length
                && header.Slice(offset, signature.Length).SequenceEqual(signature))
            {
                return UploadSanitizationResult.Unchanged();
            }
        }

        // 消息刻意笼统：不告诉上传者我们究竟按什么规则识别的。
        return UploadSanitizationResult.Reject(
            $"File content does not match the declared type '{context.Extension}'.");
    }
}
