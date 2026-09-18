namespace Tnzi.Storage.Sanitization;

/// <summary>
/// 上传前的两道闸门：<b>体积 / 扩展名白名单</b> 与 <b>净化管线</b>。
/// </summary>
/// <remarks>
/// <para>
/// 收成一个协作者而不是让每条写路径各抄一遍，是因为它们此前正是各抄各的：
/// 两道闸门只长在 <c>FileStorageService.SaveAsync</c> 上，而**分片上传的完成**与
/// **建新版本**这两条同样把字节写进存储提供者的路径一道都不过。于是一个配了
/// <c>AllowedExtensions</c> 或注册了病毒扫描器的部署，以为自己拦住了，实际上留着两个后门 ——
/// 而这类「手抄的清单会一条一条地漂」正是本仓反复记录过的失效形态。
/// </para>
/// <para>
/// ★ 净化管线（<see cref="RunAsync"/>）覆盖的是<b>每一条</b>把调用方 / 上传者的字节交给 provider 的路径：
/// 直传 <c>SaveAsync</c>、MD5 直存 <c>GetOrCreateByMd5Async</c>、解压 <c>DecompressAsync</c> 的每个条目、
/// 分片完成、建新版本 —— 五条。前一段落里说「三条」的那次提取漏掉了解压与 MD5 直存（解压只落了扩展名闸门），
/// 现由 <c>UploadSanitizationCallSiteGateTests</c> 按源码守着：任何 <c>UploadAsync</c> 调用点所在的方法都必须
/// 先跑过 <c>RunAsync</c>，除非那些字节本就来自已经落库的记录（复制 / 打包 / 缩略图 / 按原键重传 / 分片临时块）。
/// </para>
/// <para>
/// ★ 体积与扩展名<b>刻意分成两个方法</b>而不是捆在一起：分片上传是文档里明确推荐给超大文件的
/// 通道（见 <c>docs/modules/storage.md</c>），对它套用 <c>MaxFileSize</c> 会打断按文档办事的部署。
/// 那条路径因此只过扩展名与净化管线 —— 需要拦体积的场景由请求体上限在更外层兜底。
/// </para>
/// </remarks>
public sealed class UploadGuard
{
    private readonly IOptionsMonitor<StorageOptions> _options;
    private readonly IReadOnlyList<IUploadSanitizer> _sanitizers;
    private readonly ILogger<UploadGuard>? _logger;

    /// <param name="options">存储配置。</param>
    /// <param name="sanitizers">
    /// 净化器集合。**可选**：未注册任何净化器时 DI 天然给空集合，整条管线不执行、不读流、零开销。
    /// </param>
    /// <param name="logger">日志；拒绝时记一条 Warning。</param>
    public UploadGuard(
        IOptionsMonitor<StorageOptions> options,
        IEnumerable<IUploadSanitizer>? sanitizers = null,
        ILogger<UploadGuard>? logger = null)
    {
        _options = Check.NotNull(options);
        // 按 Order 排序，与 IUploadSanitizer 声明的执行次序契约一致。
        _sanitizers = sanitizers?.OrderBy(x => x.Order).ToArray() ?? [];
        _logger = logger;
    }

    private StorageOptions Options => _options.CurrentValue;

    /// <summary>体积与扩展名一起过。</summary>
    public Result<T>? Validate<T>(string fileName, long? size)
        => ValidateSize<T>(size) ?? ValidateExtension<T>(fileName);

    /// <summary>
    /// 只过体积。
    /// </summary>
    /// <param name="size">
    /// 已测得的字节数；<c>null</c> 表示量不出来（不可 seek 的流），此时<b>不拦</b> ——
    /// 拦不住也不该为此抛 <c>NotSupportedException</c>。这类请求的兜底在更外层：
    /// <c>StorageModule</c> 已按 <c>MaxFileSize</c> 放开并限制了请求体上限。
    /// </param>
    public Result<T>? ValidateSize<T>(long? size)
    {
        if (size > Options.MaxFileSize)
        {
            return Result.Failure<T>(
                $"File size ({size} bytes) exceeds maximum allowed size ({Options.MaxFileSize} bytes).",
                400,
                ErrorCodes.VALIDATION_ERROR);
        }

        return null;
    }

    /// <summary>只过扩展名白名单；白名单为空表示不限制。</summary>
    public Result<T>? ValidateExtension<T>(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (Options.AllowedExtensions.Any() &&
            !Options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<T>(
                $"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", Options.AllowedExtensions)}",
                400,
                ErrorCodes.VALIDATION_ERROR);
        }

        return null;
    }

    /// <summary>
    /// 依次执行净化管线，返回最终应交给存储提供者的流。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每个净化器执行前把流复位到起点：上一个净化器读到哪里是它的自由
    /// （见 <see cref="IUploadSanitizer"/> 的约定），不复位会让下一个读到半截内容。
    /// </para>
    /// <para>
    /// ★ 调用方必须把它跑在**算 MD5 之前、交给 provider 之前**：净化器可以改写内容
    /// （剥元数据 / 重编码），基于原始内容算出的哈希与实际落库的字节对不上 ⇒
    /// 去重会命中错误的记录，完整性校验则永远失败。
    /// </para>
    /// </remarks>
    public async Task<SanitizedUpload> RunAsync(
        string fileName, string extension, string contentType, Stream stream)
    {
        if (_sanitizers.Count == 0)
        {
            return SanitizedUpload.Passthrough(stream);
        }

        var owned = new List<Stream>();
        var current = stream;

        foreach (var sanitizer in _sanitizers)
        {
            if (current.CanSeek)
            {
                current.Position = 0;
            }

            var result = await sanitizer.SanitizeAsync(
                new UploadSanitizationContext(fileName, extension, contentType, current));

            if (result.Rejected)
            {
                _logger?.LogWarning(
                    "Upload rejected by {Sanitizer} for file {FileName}: {Reason}",
                    sanitizer.GetType().Name, fileName, result.Reason);
                return SanitizedUpload.Rejected(current, owned, result.Reason!);
            }

            if (result.Replacement != null && !ReferenceEquals(result.Replacement, current))
            {
                owned.Add(result.Replacement);
                current = result.Replacement;
            }
        }

        if (current.CanSeek)
        {
            current.Position = 0;
        }

        return SanitizedUpload.Accepted(current, owned);
    }
}

/// <summary>
/// 净化管线的产物，负责释放管线自己创建的中间流。
/// </summary>
/// <remarks>
/// 做成 <see cref="IAsyncDisposable"/> 是为了配合 <c>await using</c>：调用点通常有多个
/// 提前 return 的分支（校验失败、MD5 命中去重…），用 try/finally 逐个照顾容易漏掉一条。
/// <strong>传入的原始流不在释放范围内</strong>，它始终归调用方。
/// </remarks>
public sealed class SanitizedUpload : IAsyncDisposable
{
    private readonly List<Stream> _owned;

    private SanitizedUpload(Stream content, List<Stream> owned, bool rejected, string? reason)
    {
        Content = content;
        _owned = owned;
        IsRejected = rejected;
        Reason = reason;
    }

    /// <summary>最终应交给存储提供者的流。</summary>
    public Stream Content { get; }

    /// <summary>是否被某个净化器拒绝。</summary>
    public bool IsRejected { get; }

    /// <summary>拒绝原因。</summary>
    public string? Reason { get; }

    public static SanitizedUpload Passthrough(Stream stream) => new(stream, [], false, null);

    public static SanitizedUpload Accepted(Stream content, List<Stream> owned)
        => new(content, owned, false, null);

    public static SanitizedUpload Rejected(Stream content, List<Stream> owned, string reason)
        => new(content, owned, true, reason);

    public async ValueTask DisposeAsync()
    {
        foreach (var stream in _owned)
        {
            await stream.DisposeAsync();
        }
    }
}
