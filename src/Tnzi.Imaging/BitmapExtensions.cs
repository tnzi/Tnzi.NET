
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
    private static FontFamily GetFontFamily(string preferred = "Arial")
    {
        if (SystemFonts.TryGet(preferred, out var family))
            return family;
        var families = SystemFonts.Families;
        if (!families.Any())
            throw new InvalidOperationException("No fonts available on the system.");
        return families.First();
    }

    /// <summary>
    /// 校验图片尺寸不超过最大限制
    /// </summary>
    private static void ValidateDimensions(int width, int height)
    {
        if (width > MaxDimension || height > MaxDimension)
            throw new ArgumentException($"Image dimensions cannot exceed {MaxDimension}x{MaxDimension}.");
    }

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
            var font = fontFamily.CreateFont(fontSize, SixLabors.Fonts.FontStyle.Bold);

            // 使用 TextMeasurer 计算实际文字渲染尺寸
            var textSize = TextMeasurer.MeasureSize(text, new RichTextOptions(font));
            var textOptions = new RichTextOptions(font)
            {
                Origin = GetWatermarkPosition(image.Width, image.Height, position, (int)Math.Ceiling(textSize.Width), (int)Math.Ceiling(textSize.Height)),
                HorizontalAlignment = SixLabors.Fonts.HorizontalAlignment.Left,
                VerticalAlignment = SixLabors.Fonts.VerticalAlignment.Top
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
