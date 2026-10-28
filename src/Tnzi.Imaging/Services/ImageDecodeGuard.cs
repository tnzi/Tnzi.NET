// EXIF 的命名空间只在本文件用到（TIFF 逐页尺寸取自每页的 IFD），按 ImageEditor 的同一条理由不进全局导入。
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace Tnzi.Imaging.Services;

/// <summary>
/// 解码闸门：先读图片头拿到尺寸，超过像素上限就拒绝，绝不先解码再判断；
/// BigTIFF 在交给 ImageSharp 之前就按文件头拒绝。
/// </summary>
/// <remarks>
/// <para>
/// <b>被修复的缺陷</b>：本模块的五个解码入口与 <c>Tnzi.Storage</c> 的缩略图生成
/// 都直接 <c>Image.Load</c>，没有任何像素上限。压缩格式的字节数与解码后的内存
/// <b>没有关系</b>：一个 200KB 的 PNG 可以声明 50000×50000，解码时要 10 TB ——
/// 一个上传口就能放倒一个进程，而请求本身看起来完全正常。
/// </para>
/// <para>
/// ★ <see cref="MaxDecodePixels"/> 是<b>闸门不是调优项</b>（与
/// <c>Tnzi.Documents</c> 的 <c>MaxPagePixels</c> 同一条纪律）：页尺寸 × 分辨率没有上界，
/// 没有它，"图片处理"这个能力本身就是一个拒绝服务入口。
/// </para>
/// <para>
/// 判据是<b>解码后的像素数</b>而不是字节数：字节数只说明传了多少，说明不了要分配多少。
/// <c>Image.Identify</c> 只读文件头，代价与图片大小无关。
/// </para>
/// <para>
/// ★ <b>多帧图像按每一帧累加</b>：GIF、动画 WebP、多页 TIFF 的每一帧都是一整张画布，
/// 一张 6000×6000 的画布配一千个 1×1 的子图，是几十 KB 的上传换上百 GB 的分配，而单帧尺寸完全合规。
/// 只要一张静止画面的调用方（缩略图、遮挡编辑）走 <see cref="LoadFirstFrameAsync(Stream, long?, CancellationToken)"/>，
/// 其余帧既不识别也不解码。
/// </para>
/// <para>
/// ★ <b>BigTIFF 一律拒绝</b>（GHSA-wmxv-xphr-5c9g）。ImageSharp 3.1 读 BigTIFF 的 IFD 时，对每个声明的条目
/// 循环一次，而剩余字节不足一个条目时那次读取不前进流；条目数是 64 位的，于是公开的 24 字节样本
/// （声明 50 亿个条目）光是 <c>Image.Identify</c> 就让一个线程空转二十多秒，条目数填得更大就是无限期。修复版带构建期授权校验（见 <c>Tnzi.Imaging.csproj</c>），
/// 所以在这里按前四个字节拦下，不让 ImageSharp 看到它。ImageSharp 按内容而不是扩展名嗅探格式，
/// 因此判定同样只看内容。经典 TIFF（魔数 42，扫描件与传真件的实际形态）不受影响。
/// </para>
/// <para>
/// 拒绝时抛的是 ImageSharp 自己的 <see cref="UnknownImageFormatException"/>，与它对任何认不出的字节的回答相同：
/// 调用方原来怎么处理「这不是一张受支持的图片」，现在照样那么处理。
/// </para>
/// </remarks>
public static class ImageDecodeGuard
{
    /// <summary>判定 BigTIFF 需要的字节数：字节序标记两个字节加魔数两个字节。</summary>
    private const int ContainerSignatureLength = 4;

    /// <summary>
    /// 默认像素上限：4000 万（约 8000×5000）。
    /// </summary>
    /// <remarks>
    /// 与 <c>Tnzi.Documents</c> 的 <c>PdfRasterOptions.DefaultMaxPagePixels</c> 同量级。
    /// 覆盖常见相机与扫描件，挡住的是那些只可能来自攻击或误操作的尺寸。
    /// </remarks>
    public const long DefaultMaxDecodePixels = 40_000_000L;

    private static long _maxDecodePixels = DefaultMaxDecodePixels;

    /// <summary>
    /// 当前像素上限（0 或负数表示不限制）。
    /// </summary>
    /// <remarks>
    /// 由 <c>ImagingModule</c> 在启动时按 <c>Imaging:MaxDecodePixels</c> 设置一次。
    /// 做成静态是因为解码入口是静态扩展方法（<c>BitmapExtensions</c>），
    /// 而闸门必须覆盖<b>所有</b>入口 —— 只有拿得到 DI 的调用方才受保护，
    /// 等于把最容易被忘记的那些入口留在外面。
    /// </remarks>
    public static long MaxDecodePixels
    {
        get => _maxDecodePixels;
        set => _maxDecodePixels = value;
    }

    /// <summary>
    /// 按上限解码字节数组（全部帧）。
    /// </summary>
    public static async Task<Image<Rgba32>> LoadAsync(
        byte[] bytes,
        long? maxPixels = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(bytes);

        using var stream = new MemoryStream(bytes, writable: false);
        return await LoadAsync(stream, maxPixels, cancellationToken);
    }

    /// <summary>
    /// 按上限解码流（全部帧）。
    /// </summary>
    /// <remarks>
    /// 不可定位的流（对象存储的响应流就是这样）先缓冲成内存流再判断：缓冲的是<b>压缩后</b>
    /// 的字节，那部分本来就已经受上传大小限制约束；真正会撑爆进程的是解码后的位图。
    /// </remarks>
    public static Task<Image<Rgba32>> LoadAsync(
        Stream stream,
        long? maxPixels = null,
        CancellationToken cancellationToken = default)
        => LoadCoreAsync(stream, maxPixels ?? MaxDecodePixels, firstFrameOnly: false, cancellationToken);

    /// <summary>
    /// 按上限只解码首帧（字节数组）。
    /// </summary>
    /// <remarks>
    /// 给只要一张静止画面的调用方：缩略图、遮挡编辑、读码。动画与多页文档的其余帧既不识别也不解码，
    /// 闸门按首帧一张画布判上限。
    /// </remarks>
    public static async Task<Image<Rgba32>> LoadFirstFrameAsync(
        byte[] bytes,
        long? maxPixels = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(bytes);

        using var stream = new MemoryStream(bytes, writable: false);
        return await LoadFirstFrameAsync(stream, maxPixels, cancellationToken);
    }

    /// <inheritdoc cref="LoadFirstFrameAsync(byte[], long?, CancellationToken)"/>
    public static Task<Image<Rgba32>> LoadFirstFrameAsync(
        Stream stream,
        long? maxPixels = null,
        CancellationToken cancellationToken = default)
        => LoadCoreAsync(stream, maxPixels ?? MaxDecodePixels, firstFrameOnly: true, cancellationToken);

    private static async Task<Image<Rgba32>> LoadCoreAsync(
        Stream stream,
        long limit,
        bool firstFrameOnly,
        CancellationToken cancellationToken)
    {
        Check.NotNull(stream);

        var seekable = stream;
        MemoryStream? buffered = null;

        try
        {
            if (!stream.CanSeek)
            {
                buffered = new MemoryStream();
                await stream.CopyToAsync(buffered, cancellationToken);
                buffered.Position = 0;
                seekable = buffered;
            }

            var start = seekable.Position;
            await ThrowIfBigTiffAsync(seekable, cancellationToken);
            var info = await Image.IdentifyAsync(IdentifyOptions(firstFrameOnly), seekable, cancellationToken);
            seekable.Position = start;

            var decodeOptions = PlanDecode(info, limit, firstFrameOnly);

            return await Image.LoadAsync<Rgba32>(decodeOptions, seekable, cancellationToken);
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    /// <summary>
    /// 同步版本，供既有的同步解码入口使用（全部帧）。
    /// </summary>
    public static Image<Rgba32> Load(Stream stream, long? maxPixels = null)
    {
        Check.NotNull(stream);

        var limit = maxPixels ?? MaxDecodePixels;

        if (!stream.CanSeek)
        {
            using var buffered = new MemoryStream();
            stream.CopyTo(buffered);
            buffered.Position = 0;
            return Load(buffered, limit);
        }

        var start = stream.Position;
        ThrowIfBigTiff(stream);
        var info = Image.Identify(IdentifyOptions(firstFrameOnly: false), stream);
        stream.Position = start;

        var decodeOptions = PlanDecode(info, limit, firstFrameOnly: false);

        return Image.Load<Rgba32>(decodeOptions, stream);
    }

    /// <summary>
    /// 同步版本（字节数组）。
    /// </summary>
    public static Image<Rgba32> Load(byte[] bytes, long? maxPixels = null)
    {
        Check.NotNullOrEmpty(bytes);

        using var stream = new MemoryStream(bytes, writable: false);
        return Load(stream, maxPixels);
    }

    /// <summary>
    /// 字节开头是 BigTIFF 的签名时抛 <see cref="UnknownImageFormatException"/>。
    /// </summary>
    /// <remarks>
    /// 给已经握着整段字节的解码入口用（<see cref="QrCodeReader"/>）。不足四个字节时不判，
    /// 留给 ImageSharp 按原来的语义回答。
    /// </remarks>
    internal static void ThrowIfBigTiff(ReadOnlySpan<byte> header)
    {
        if (IsBigTiff(header))
        {
            throw new UnknownImageFormatException(
                "BigTIFF (64-bit TIFF) images are not supported. Save the image as a classic TIFF, PNG or JPEG.");
        }
    }

    /// <summary>
    /// 读可定位流的前四个字节判定 BigTIFF，判完把位置放回原处。
    /// </summary>
    internal static void ThrowIfBigTiff(Stream seekable)
    {
        Span<byte> header = stackalloc byte[ContainerSignatureLength];
        var start = seekable.Position;
        var read = seekable.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        seekable.Position = start;

        ThrowIfBigTiff(header[..read]);
    }

    /// <inheritdoc cref="ThrowIfBigTiff(Stream)"/>
    internal static async Task ThrowIfBigTiffAsync(Stream seekable, CancellationToken cancellationToken)
    {
        var header = new byte[ContainerSignatureLength];
        var start = seekable.Position;
        var read = await seekable.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        seekable.Position = start;

        ThrowIfBigTiff(header.AsSpan(0, read));
    }

    /// <summary>
    /// 小端 <c>II 2B 00</c> 或大端 <c>MM 00 2B</c>。
    /// </summary>
    /// <remarks>
    /// 只看字节序标记与魔数，比 ImageSharp 自己的识别（还要求随后的偏移量字节数为 8）更宽：
    /// 宽出来的那部分不是任何一种受支持的格式，ImageSharp 本来也只会回答「不认识」。
    /// </remarks>
    private static bool IsBigTiff(ReadOnlySpan<byte> header) =>
        header.Length >= ContainerSignatureLength
        && ((header[0] == (byte)'I' && header[1] == (byte)'I' && header[2] == 0x2B && header[3] == 0x00)
            || (header[0] == (byte)'M' && header[1] == (byte)'M' && header[2] == 0x00 && header[3] == 0x2B));

    /// <summary>只识别、只解码首帧。</summary>
    private static readonly DecoderOptions FirstFrameOnly = new() { MaxFrames = 1 };

    /// <summary>
    /// 按 MIME 类型认 TIFF，而不是引用 ImageSharp 的 TIFF 格式类型：后者正是被压制的 TIFF 编码器告警的可达入口，
    /// <c>ImageSharpAdvisoryGateTests</c> 不允许它出现在 <c>src/</c>。
    /// </summary>
    private const string TiffMimeType = "image/tiff";

    private static DecoderOptions IdentifyOptions(bool firstFrameOnly) =>
        firstFrameOnly ? FirstFrameOnly : new DecoderOptions();

    /// <summary>
    /// 按识别结果算出解码后的总像素，超过上限就拒绝；放行时把解码器的帧数钉在识别过的那些帧上。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>每一帧都是一整张画布</b>：GIF 的一帧哪怕只是一个 1×1 的子图，解码器也给它分配整幅画布，
    /// 所以总像素是逐帧累加，而不是只看画布一次。
    /// </para>
    /// <para>
    /// TIFF 的每一页按它自己声明的尺寸分配：页尺寸不一致时 ImageSharp 先把每一页都解出来，
    /// 最后才报「不支持」，于是一份首页 4×4、次页三万见方的几百字节文件，在被拒绝之前已经分配了几个 GB。
    /// 这里按「页自己声明的尺寸与画布取大」逐页累加。
    /// </para>
    /// <para>
    /// 解码选项的 <c>MaxFrames</c> 取识别出的帧数：闸门只为它数过、量过的那些帧放行，
    /// 识别阶段若因为任何原因少数了帧，多出来的那些也不会被解出来。
    /// </para>
    /// </remarks>
    private static DecoderOptions PlanDecode(ImageInfo info, long limit, bool firstFrameOnly)
    {
        var canvas = (long)info.Width * info.Height;
        var frames = firstFrameOnly ? 1 : Math.Max(1, info.FrameMetadataCollection.Count);
        var isTiff = string.Equals(info.Metadata.DecodedImageFormat?.DefaultMimeType, TiffMimeType, StringComparison.OrdinalIgnoreCase);

        long pixels = 0;
        for (var i = 0; i < frames; i++)
        {
            var framePixels = isTiff ? Math.Max(canvas, DeclaredTiffPagePixels(info, i)) : canvas;
            pixels = pixels > long.MaxValue - framePixels ? long.MaxValue : pixels + framePixels;
        }

        if (limit > 0 && pixels > limit)
        {
            throw new ImageDecodeLimitExceededException(info.Width, info.Height, frames, pixels, limit);
        }

        return firstFrameOnly ? FirstFrameOnly : new DecoderOptions { MaxFrames = (uint)frames };
    }

    /// <summary>
    /// TIFF 第 <paramref name="index"/> 页在自己的 IFD 里声明的像素数；读不到时为 0（按画布计）。
    /// </summary>
    private static long DeclaredTiffPagePixels(ImageInfo info, int index)
    {
        if (index >= info.FrameMetadataCollection.Count)
            return 0;

        var exif = info.FrameMetadataCollection[index].ExifProfile;
        if (exif is null
            || !exif.TryGetValue(ExifTag.ImageWidth, out var width)
            || !exif.TryGetValue(ExifTag.ImageLength, out var height))
        {
            return 0;
        }

        var declared = (ulong)(uint)width.Value * (uint)height.Value;
        return declared > long.MaxValue ? long.MaxValue : (long)declared;
    }
}

/// <summary>
/// 图片超过解码像素上限。
/// </summary>
/// <remarks>
/// 刻意是<b>业务异常</b>而不是崩溃：这是一次可以被拒绝的请求，
/// 调用方应当把它变成 400 而不是 500。
/// </remarks>
public class ImageDecodeLimitExceededException : BusinessException
{
    /// <summary>
    /// 初始化一个 <see cref="ImageDecodeLimitExceededException"/> 类型的新实例。
    /// </summary>
    public ImageDecodeLimitExceededException(int width, int height, long pixels, long limit)
        : this(width, height, frames: 1, pixels, limit)
    {
    }

    /// <summary>
    /// 初始化一个 <see cref="ImageDecodeLimitExceededException"/> 类型的新实例（多帧图像）。
    /// </summary>
    public ImageDecodeLimitExceededException(int width, int height, int frames, long pixels, long limit)
        : base(
            frames > 1
                ? $"The image is {width}x{height} with {frames} frames ({pixels} pixels decoded), above the {limit} pixel decode limit."
                : $"The image is {width}x{height} ({pixels} pixels), above the {limit} pixel decode limit.",
            ErrorCodes.VALIDATION_ERROR,
            400)
    {
        Width = width;
        Height = height;
        Frames = frames;
        Pixels = pixels;
        Limit = limit;
    }

    /// <summary>声明的宽度。</summary>
    public int Width { get; }

    /// <summary>声明的高度。</summary>
    public int Height { get; }

    /// <summary>要解码的帧数（每一帧都是一整张画布）。</summary>
    public int Frames { get; }

    /// <summary>要解码的像素总数（帧数 × 宽 × 高）。</summary>
    public long Pixels { get; }

    /// <summary>当前上限。</summary>
    public long Limit { get; }
}
