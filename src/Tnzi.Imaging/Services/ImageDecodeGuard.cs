namespace Tnzi.Imaging.Services;

/// <summary>
/// 解码闸门：先读图片头拿到尺寸，超过像素上限就拒绝，绝不先解码再判断。
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
/// </remarks>
public static class ImageDecodeGuard
{
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
    /// 按上限解码字节数组。
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
    /// 按上限解码流。
    /// </summary>
    /// <remarks>
    /// 不可定位的流（对象存储的响应流就是这样）先缓冲成内存流再判断：缓冲的是<b>压缩后</b>
    /// 的字节，那部分本来就已经受上传大小限制约束；真正会撑爆进程的是解码后的位图。
    /// </remarks>
    public static async Task<Image<Rgba32>> LoadAsync(
        Stream stream,
        long? maxPixels = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(stream);

        var limit = maxPixels ?? MaxDecodePixels;

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
            var info = await Image.IdentifyAsync(seekable, cancellationToken);
            seekable.Position = start;

            EnsureWithinLimit(info.Width, info.Height, limit);

            return await Image.LoadAsync<Rgba32>(seekable, cancellationToken);
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    /// <summary>
    /// 同步版本，供既有的同步解码入口使用。
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
        var info = Image.Identify(stream);
        stream.Position = start;

        EnsureWithinLimit(info.Width, info.Height, limit);

        return Image.Load<Rgba32>(stream);
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

    private static void EnsureWithinLimit(int width, int height, long limit)
    {
        if (limit <= 0)
            return;

        var pixels = (long)width * height;
        if (pixels > limit)
        {
            throw new ImageDecodeLimitExceededException(width, height, pixels, limit);
        }
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
        : base(
            $"The image is {width}x{height} ({pixels} pixels), above the {limit} pixel decode limit.",
            ErrorCodes.VALIDATION_ERROR,
            400)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Limit = limit;
    }

    /// <summary>声明的宽度。</summary>
    public int Width { get; }

    /// <summary>声明的高度。</summary>
    public int Height { get; }

    /// <summary>声明的像素总数。</summary>
    public long Pixels { get; }

    /// <summary>当前上限。</summary>
    public long Limit { get; }
}
