
namespace Tnzi.Imaging;

/// <summary>
/// 图片处理扩展方法（使用SixLabors.ImageSharp实现，支持跨平台）
/// </summary>
public static class BitmapExtensions
{
    /// <summary>
    /// 图片最大允许尺寸（宽/高）
    /// </summary>
    private const int MaxDimension = 10000;

    /// <summary>
    /// 获取可用的字体族，优先使用指定字体，不可用时回退到系统任意可用字体
    /// </summary>
    private static readonly string[] FontFallbacks =
        ["Arial", "Liberation Sans", "DejaVu Sans", "Noto Sans", "Verdana"];

    private static FontFamily GetFontFamily(string preferred = "Arial")
    {
        // 优先使用指定字体
        if (SystemFonts.TryGet(preferred, out var family) && IsFontUsable(family))
            return family;
        // 回退列表
        foreach (var name in FontFallbacks)
        {
            if (name != preferred && SystemFonts.TryGet(name, out family) && IsFontUsable(family))
                return family;
        }
        // 最终回退：遍历所有系统字体，跳过 CFF 字体
        foreach (var f in SystemFonts.Families)
        {
            if (IsFontUsable(f))
                return f;
        }
        throw new InvalidOperationException("No usable TrueType fonts available on the system.");
    }

    /// <summary>
    /// 检测字体是否可用（部分字体缺少 TrueType 所需的 loca 表）
    /// </summary>
    private static bool IsFontUsable(FontFamily family)
    {
        try
        {
            var font = family.CreateFont(12, FontStyle.Regular);
            TextMeasurer.MeasureSize("A", new TextOptions(font));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 校验图片尺寸不超过最大限制
    /// </summary>
    private static void ValidateDimensions(int width, int height)
    {
        if (width > MaxDimension || height > MaxDimension)
            throw new ArgumentException($"Image dimensions cannot exceed {MaxDimension}x{MaxDimension}.");
    }

    #region Load - 图片加载

    /// <summary>
    /// 从字节数组加载图片
    /// </summary>
    /// <param name="bytes">图片字节数组</param>
    /// <returns>图片对象（调用方负责 Dispose）</returns>
    public static Image<Rgba32> LoadFromBytes(byte[] bytes)
    {
        Check.NotNullOrEmpty(bytes);
        return Image.Load<Rgba32>(bytes);
    }

    /// <summary>
    /// 从流加载图片
    /// </summary>
    /// <param name="stream">图片流</param>
    /// <returns>图片对象（调用方负责 Dispose）</returns>
    public static Image<Rgba32> LoadFromStream(Stream stream)
    {
        Check.NotNull(stream);
        return Image.Load<Rgba32>(stream);
    }

    /// <summary>
    /// 从字节数组异步加载图片
    /// </summary>
    /// <param name="bytes">图片字节数组</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>图片对象（调用方负责 Dispose）</returns>
    public static async Task<Image<Rgba32>> LoadFromBytesAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(bytes);
        using var ms = new MemoryStream(bytes);
        return await Image.LoadAsync<Rgba32>(ms, cancellationToken);
    }

    /// <summary>
    /// 从流异步加载图片
    /// </summary>
    /// <param name="stream">图片流</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>图片对象（调用方负责 Dispose）</returns>
    public static async Task<Image<Rgba32>> LoadFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        Check.NotNull(stream);
        return await Image.LoadAsync<Rgba32>(stream, cancellationToken);
    }

    #endregion

    #region Resize - 图片缩放

    /// <summary>
    /// 按指定尺寸缩放图片
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="width">目标宽度</param>
    /// <param name="height">目标高度</param>
    /// <param name="mode">缩放模式</param>
    /// <returns>缩放后的图片</returns>
    public static Image<Rgba32> Resize(this Image<Rgba32> image, int width, int height, ResizeMode mode = ResizeMode.Max)
    {
        Check.NotNull(image);
        Check.GreaterThan(width, 0);
        Check.GreaterThan(height, 0);
        ValidateDimensions(width, height);

        var clone = image.Clone();
        clone.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(width, height),
            Mode = mode
        }));
        return clone;
    }

    /// <summary>
    /// 按比例缩放图片
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="scale">缩放比例（0-1之间）</param>
    /// <returns>缩放后的图片</returns>
    public static Image<Rgba32> Resize(this Image<Rgba32> image, float scale)
    {
        Check.NotNull(image);
        if (scale <= 0 || scale > 1)
            throw new ArgumentException("Scale must be between 0 and 1.", nameof(scale));

        int newWidth = (int)(image.Width * scale);
        int newHeight = (int)(image.Height * scale);
        return Resize(image, newWidth, newHeight);
    }

    /// <summary>
    /// 按最大尺寸缩放（保持宽高比）
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="maxWidth">最大宽度</param>
    /// <param name="maxHeight">最大高度</param>
    /// <returns>缩放后的图片</returns>
    public static Image<Rgba32> ResizeToFit(this Image<Rgba32> image, int maxWidth, int maxHeight)
    {
        Check.NotNull(image);

        double ratio = Math.Min((double)maxWidth / image.Width, (double)maxHeight / image.Height);
        if (ratio >= 1.0)
            return image.Clone();

        int newWidth = (int)(image.Width * ratio);
        int newHeight = (int)(image.Height * ratio);
        return Resize(image, newWidth, newHeight);
    }

    #endregion

    #region Crop - 图片裁剪

    /// <summary>
    /// 裁剪图片
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="x">起始X坐标</param>
    /// <param name="y">起始Y坐标</param>
    /// <param name="width">裁剪宽度</param>
    /// <param name="height">裁剪高度</param>
    /// <returns>裁剪后的图片</returns>
    public static Image<Rgba32> Crop(this Image<Rgba32> image, int x, int y, int width, int height)
    {
        Check.NotNull(image);
        ValidateDimensions(width, height);
        if (x < 0 || y < 0 || x + width > image.Width || y + height > image.Height)
            throw new ArgumentException("Crop parameters are out of image bounds.");

        var clone = image.Clone();
        clone.Mutate(ctx => ctx.Crop(new Rectangle(x, y, width, height)));
        return clone;
    }

    /// <summary>
    /// 从中心裁剪图片
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="width">裁剪宽度</param>
    /// <param name="height">裁剪高度</param>
    /// <returns>裁剪后的图片</returns>
    public static Image<Rgba32> CropFromCenter(this Image<Rgba32> image, int width, int height)
    {
        Check.NotNull(image);

        int x = (image.Width - width) / 2;
        int y = (image.Height - height) / 2;
        return Crop(image, x, y, width, height);
    }

    #endregion

    #region Compress - 图片压缩

    /// <summary>
    /// 压缩图片（JPEG格式）
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="quality">压缩质量（1-100）</param>
    /// <returns>压缩后的图片字节数组</returns>
    public static byte[] CompressAsJpeg(this Image<Rgba32> image, int quality = 85)
    {
        Check.NotNull(image);
        Check.InRange(quality, 1, 100);

        using var ms = new MemoryStream();
        var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
        {
            Quality = quality
        };
        image.SaveAsJpeg(ms, encoder);
        return ms.ToArray();
    }

    /// <summary>
    /// 压缩图片（PNG格式）
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="compressionLevel">压缩级别（0-9）</param>
    /// <returns>压缩后的图片字节数组</returns>
    public static byte[] CompressAsPng(this Image<Rgba32> image, int compressionLevel = 6)
    {
        Check.NotNull(image);
        Check.InRange(compressionLevel, 0, 9);

        using var ms = new MemoryStream();
        var encoder = new SixLabors.ImageSharp.Formats.Png.PngEncoder
        {
            CompressionLevel = (SixLabors.ImageSharp.Formats.Png.PngCompressionLevel)compressionLevel
        };
        image.SaveAsPng(ms, encoder);
        return ms.ToArray();
    }

    #endregion

    #region Watermark - 水印

    /// <summary>
    /// 添加文字水印
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="text">水印文字</param>
    /// <param name="fontSize">字体大小</param>
    /// <param name="color">文字颜色</param>
    /// <param name="position">水印位置</param>
    /// <param name="opacity">透明度（0-1）</param>
    /// <returns>添加水印后的图片</returns>
    public static Image<Rgba32> AddTextWatermark(
        this Image<Rgba32> image,
        string text,
        int fontSize = 20,
        Color color = default,
        WatermarkPosition position = WatermarkPosition.BottomRight,
        float opacity = 0.5f)
    {
        Check.NotNull(image);
        Check.NotNullOrEmpty(text);
        Check.InRange(opacity, 0f, 1f);

        if (color == default)
            color = Color.White;

        var clone = image.Clone();
        clone.Mutate(ctx =>
        {
            // 使用字体回退机制
            var fontFamily = GetFontFamily("Arial");
            var font = fontFamily.CreateFont(fontSize, FontStyle.Bold);

            // 使用 TextMeasurer 计算实际文字渲染尺寸
            var textSize = TextMeasurer.MeasureSize(text, new RichTextOptions(font));
            var textOptions = new RichTextOptions(font)
            {
                Origin = GetWatermarkPosition(image.Width, image.Height, position, (int)Math.Ceiling(textSize.Width), (int)Math.Ceiling(textSize.Height)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            // 应用透明度
            var watermarkColor = color.WithAlpha(opacity);
            ctx.DrawText(textOptions, text, watermarkColor);
        });

        return clone;
    }

    /// <summary>
    /// 添加图片水印
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="watermarkImage">水印图片</param>
    /// <param name="position">水印位置</param>
    /// <param name="opacity">透明度（0-1）</param>
    /// <returns>添加水印后的图片</returns>
    public static Image<Rgba32> AddImageWatermark(
        this Image<Rgba32> image,
        Image<Rgba32> watermarkImage,
        WatermarkPosition position = WatermarkPosition.BottomRight,
        float opacity = 0.5f)
    {
        Check.NotNull(image);
        Check.NotNull(watermarkImage);
        Check.InRange(opacity, 0f, 1f);

        var clone = image.Clone();
        clone.Mutate(ctx =>
        {
            var positionPoint = GetWatermarkPosition(
                image.Width, image.Height,
                position,
                watermarkImage.Width, watermarkImage.Height);

            // 绘制水印图片 - 使用Rectangle定位，并通过GraphicsOptions设置透明度
            var destRect = new Rectangle(
                (int)positionPoint.X,
                (int)positionPoint.Y,
                watermarkImage.Width,
                watermarkImage.Height);

            var graphicsOptions = new GraphicsOptions
            {
                ColorBlendingMode = PixelColorBlendingMode.Normal,
                AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver,
                BlendPercentage = opacity
            };

            ctx.DrawImage(watermarkImage, destRect, graphicsOptions);
        });

        return clone;
    }

    private static PointF GetWatermarkPosition(int imageWidth, int imageHeight, WatermarkPosition position, int watermarkWidth, int watermarkHeight)
    {
        return position switch
        {
            WatermarkPosition.TopLeft => new PointF(10, 10),
            WatermarkPosition.TopRight => new PointF(imageWidth - watermarkWidth - 10, 10),
            WatermarkPosition.BottomLeft => new PointF(10, imageHeight - watermarkHeight - 10),
            WatermarkPosition.BottomRight => new PointF(imageWidth - watermarkWidth - 10, imageHeight - watermarkHeight - 10),
            WatermarkPosition.Center => new PointF((imageWidth - watermarkWidth) / 2f, (imageHeight - watermarkHeight) / 2f),
            _ => new PointF(imageWidth - watermarkWidth - 10, imageHeight - watermarkHeight - 10)
        };
    }

    #endregion

    #region Thumbnail - 缩略图

    /// <summary>
    /// 生成缩略图
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="width">缩略图宽度</param>
    /// <param name="height">缩略图高度</param>
    /// <returns>缩略图</returns>
    public static Image<Rgba32> GenerateThumbnail(this Image<Rgba32> image, int width, int height)
    {
        Check.NotNull(image);

        return ResizeToFit(image, width, height);
    }

    /// <summary>
    /// 生成正方形缩略图（从中心裁剪）
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="size">缩略图尺寸</param>
    /// <returns>缩略图</returns>
    public static Image<Rgba32> GenerateSquareThumbnail(this Image<Rgba32> image, int size)
    {
        Check.NotNull(image);

        int minSize = Math.Min(image.Width, image.Height);
        using var cropped = CropFromCenter(image, minSize, minSize);
        return Resize(cropped, size, size);
    }

    #endregion

    #region Format Detection - 格式检测

    /// <summary>
    /// Detect image format from raw bytes by inspecting magic bytes (file signature).
    /// Returns null if the format is not recognized.
    /// </summary>
    /// <param name="bytes">Image file bytes (at least first 12 bytes needed)</param>
    /// <returns>Detected image format info, or null if not recognized</returns>
    public static ImageFormatInfo? DetectFormat(byte[] bytes)
    {
        Check.NotNullOrEmpty(bytes);

        if (bytes.Length < 4)
            return null;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return new ImageFormatInfo("PNG", "image/png", ".png");

        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return new ImageFormatInfo("JPEG", "image/jpeg", ".jpg");

        // GIF: 47 49 46 38
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
            return new ImageFormatInfo("GIF", "image/gif", ".gif");

        // BMP: 42 4D
        if (bytes[0] == 0x42 && bytes[1] == 0x4D)
            return new ImageFormatInfo("BMP", "image/bmp", ".bmp");

        // WebP: RIFF....WEBP
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return new ImageFormatInfo("WebP", "image/webp", ".webp");

        // TIFF: 49 49 2A 00 (little-endian) or 4D 4D 00 2A (big-endian)
        if ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00)
            || (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A))
            return new ImageFormatInfo("TIFF", "image/tiff", ".tiff");

        // ICO: 00 00 01 00
        if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00)
            return new ImageFormatInfo("ICO", "image/x-icon", ".ico");

        return null;
    }

    /// <summary>
    /// Detect image format from a stream by reading the first bytes.
    /// Stream position is restored after detection.
    /// </summary>
    /// <param name="stream">Image stream (must be seekable)</param>
    /// <returns>Detected image format info, or null if not recognized</returns>
    public static ImageFormatInfo? DetectFormat(Stream stream)
    {
        Check.NotNull(stream);

        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable for format detection.", nameof(stream));

        var originalPosition = stream.Position;
        var header = new byte[12];
        var bytesRead = stream.Read(header, 0, 12);
        stream.Position = originalPosition;

        if (bytesRead < 4)
            return null;

        return DetectFormat(header);
    }

    #endregion

    #region Image Hash - 图片指纹

    /// <summary>
    /// Compute a perceptual hash (pHash) of an image for deduplication and similarity detection.
    /// <para>
    /// Algorithm: resize to 8x8 grayscale, compute mean, generate 64-bit hash based on pixel > mean.
    /// Two images are considered similar if their hash Hamming distance is below a threshold (e.g., 5).
    /// </para>
    /// </summary>
    /// <param name="image">Source image</param>
    /// <returns>64-bit perceptual hash as hex string (16 characters)</returns>
    public static string ComputePerceptualHash(this Image<Rgba32> image)
    {
        Check.NotNull(image);

        // 1. Resize to 8x8
        using var small = image.Clone();
        small.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(8, 8),
            Mode = ResizeMode.Stretch
        }));

        // 2. Convert to grayscale values
        var pixels = new double[64];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var pixel = small[x, y];
                // ITU-R BT.709 luminance formula
                pixels[y * 8 + x] = 0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B;
            }
        }

        // 3. Compute mean
        var mean = pixels.Average();

        // 4. Generate hash: each bit = pixel > mean
        ulong hash = 0;
        for (var i = 0; i < 64; i++)
        {
            if (pixels[i] > mean)
                hash |= 1UL << i;
        }

        return hash.ToString("x16");
    }

    /// <summary>
    /// Compute the Hamming distance between two perceptual hashes.
    /// Lower values indicate more similar images.
    /// </summary>
    /// <param name="hash1">First hash (16-char hex string)</param>
    /// <param name="hash2">Second hash (16-char hex string)</param>
    /// <returns>Hamming distance (0 = identical, 64 = completely different)</returns>
    public static int HammingDistance(string hash1, string hash2)
    {
        Check.NotNullOrWhiteSpace(hash1);
        Check.NotNullOrWhiteSpace(hash2);

        if (hash1.Length != 16 || hash2.Length != 16)
            throw new ArgumentException("Hashes must be 16-character hex strings.");

        var val1 = Convert.ToUInt64(hash1, 16);
        var val2 = Convert.ToUInt64(hash2, 16);

        var xor = val1 ^ val2;
        var distance = 0;
        while (xor != 0)
        {
            distance++;
            xor &= xor - 1; // Clear least significant set bit
        }

        return distance;
    }

    #endregion

    #region Format Conversion - 格式转换

    /// <summary>
    /// 转换为JPEG格式
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="quality">质量（1-100）</param>
    /// <returns>JPEG格式的字节数组</returns>
    public static byte[] ToJpeg(this Image<Rgba32> image, int quality = 90)
    {
        Check.NotNull(image);

        using var ms = new MemoryStream();
        var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
        {
            Quality = quality
        };
        image.SaveAsJpeg(ms, encoder);
        return ms.ToArray();
    }

    /// <summary>
    /// 转换为PNG格式
    /// </summary>
    /// <param name="image">图片</param>
    /// <returns>PNG格式的字节数组</returns>
    public static byte[] ToPng(this Image<Rgba32> image)
    {
        Check.NotNull(image);

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// 转换为WebP格式
    /// </summary>
    /// <param name="image">图片</param>
    /// <param name="quality">质量（1-100）</param>
    /// <returns>WebP格式的字节数组</returns>
    public static byte[] ToWebP(this Image<Rgba32> image, int quality = 90)
    {
        Check.NotNull(image);

        using var ms = new MemoryStream();
        var encoder = new SixLabors.ImageSharp.Formats.Webp.WebpEncoder
        {
            Quality = quality
        };
        image.SaveAsWebp(ms, encoder);
        return ms.ToArray();
    }

    #endregion
}
