// ImageSharp 的编码器命名空间刻意只在文件级导入：每种格式一个命名空间，
// 里面清一色 *Encoder / *Decoder / *Metadata，全局导入等于把十几个格式的同形类型
// 一起摊进整个程序集。
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;

namespace Tnzi.Imaging.Services;

/// <inheritdoc cref="IImageEditor"/>
/// <remarks>
/// 基于 ImageSharp。<b>全程在内存里做，不落任何临时文件</b> ——
/// 这个接口存在的场景通常正是「原件不许离开受控环境」，往磁盘写一份中间产物
/// 会在那条纪律上开一个谁都不会记得关的口子。
/// </remarks>
public class ImageEditor : IImageEditor
{
    /// <summary>本实现认得的扩展名。</summary>
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };

    /// <inheritdoc />
    public bool CanEdit(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return SupportedExtensions.Contains(Path.GetExtension(fileName));
    }

    /// <inheritdoc />
    public async Task<EditedImage> EditAsync(
        byte[] source,
        string sourceFileName,
        ImageEditRequest request,
        CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(source);
        Check.NotNullOrWhiteSpace(sourceFileName);
        Check.NotNull(request);

        if (!CanEdit(sourceFileName))
        {
            throw new NotSupportedException($"Image editing is not supported for '{Path.GetExtension(sourceFileName)}'.");
        }

        ValidateRects(request);

        using var image = await Image.LoadAsync<Rgba32>(new MemoryStream(source), cancellationToken);

        // 顺序固定：遮挡 -> 模糊 -> 裁切 -> 缩放。全部坐标相对源图，因此换算一次即可，
        // 且缩放排在最后 —— 先缩放再遮挡会让遮挡落在插值过的像素上，边缘可能残留原始信息。
        var width = image.Width;
        var height = image.Height;

        using var edited = Apply(image, request, width, height);

        var format = request.OutputFormat ?? FormatOf(sourceFileName);
        var content = await EncodeAsync(edited, format, request.Quality, cancellationToken);

        return new EditedImage(content, format, edited.Width, edited.Height);
    }

    /// <summary>
    /// 依次施加四种操作，每一步都产出新图，前一步的中间产物就地释放。
    /// </summary>
    private static Image<Rgba32> Apply(Image<Rgba32> image, ImageEditRequest request, int width, int height)
    {
        var current = image.Clone();

        if (request.Redactions.Count > 0)
        {
            var fill = ParseColor(request.RedactionColor);
            var next = current.Redact(request.Redactions.Select(r => ToPixels(r, width, height)), fill);
            current.Dispose();
            current = next;
        }

        foreach (var region in request.BlurRegions)
        {
            var next = current.BlurRegion(ToPixels(region, width, height), request.BlurSigma);
            current.Dispose();
            current = next;
        }

        if (request.Crop is { } crop)
        {
            var box = ToPixels(crop, width, height);
            var next = current.Crop(box.X, box.Y, box.Width, box.Height);
            current.Dispose();
            current = next;
        }

        if (request.MaxWidth > 0 || request.MaxHeight > 0)
        {
            // 只给了一边时，另一边取「不限」——用当前尺寸即可，ResizeToFit 取两者中较小的比例。
            var maxWidth = request.MaxWidth > 0 ? request.MaxWidth : current.Width;
            var maxHeight = request.MaxHeight > 0 ? request.MaxHeight : current.Height;

            var next = current.ResizeToFit(maxWidth, maxHeight);
            current.Dispose();
            current = next;
        }

        return current;
    }

    /// <summary>
    /// 归一化矩形 → 像素矩形。四舍五入到最近的整数像素，并保证宽高至少为 1。
    /// </summary>
    private static Rectangle ToPixels(NormalizedRect rect, int width, int height)
    {
        var x = (int)Math.Round(rect.X * width);
        var y = (int)Math.Round(rect.Y * height);
        var w = Math.Max(1, (int)Math.Round(rect.Width * width));
        var h = Math.Max(1, (int)Math.Round(rect.Height * height));

        // 舍入可能把右下角推出边界一个像素，这里收回来。
        w = Math.Min(w, width - x);
        h = Math.Min(h, height - y);

        return new Rectangle(x, y, w, h);
    }

    /// <summary>
    /// 越界的坐标一律拒绝，不做「尽力而为」的裁剪。
    /// </summary>
    /// <remarks>
    /// 越界几乎总是坐标系搞错了（拿像素当归一化、或者 Y 轴没翻），
    /// 而它的表现是「遮挡画在了图外」这种安静的失效 —— 一张没遮干净的图比一个异常危险得多。
    /// </remarks>
    private static void ValidateRects(ImageEditRequest request)
    {
        foreach (var rect in request.Redactions.Concat(request.BlurRegions))
        {
            EnsureWithinUnitSquare(rect, "Redaction/blur region");
        }

        if (request.Crop is { } crop)
        {
            EnsureWithinUnitSquare(crop, "Crop region");
        }

        if (request.Quality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Quality must be between 1 and 100.");
        }
    }

    private static void EnsureWithinUnitSquare(NormalizedRect rect, string what)
    {
        if (!rect.IsWithinUnitSquare())
        {
            throw new ArgumentOutOfRangeException(
                nameof(rect),
                $"{what} must be normalized to 0-1 with a positive size; got {rect}.");
        }
    }

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        return Color.TryParseHex(hex, out var color)
            ? color
            : throw new ArgumentException($"'{hex}' is not a valid hex color.", nameof(hex));
    }

    private static ImageOutputFormat FormatOf(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => ImageOutputFormat.Png,
            ".webp" => ImageOutputFormat.WebP,
            // 其余（bmp/gif/tiff）没有对应的输出选项，统一转 JPEG：
            // 编辑产物是给人看的派生物，不是归档原件。
            _ => ImageOutputFormat.Jpeg
        };

    private static async Task<byte[]> EncodeAsync(
        Image<Rgba32> image,
        ImageOutputFormat format,
        int quality,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();

        switch (format)
        {
            case ImageOutputFormat.Png:
                await image.SaveAsPngAsync(stream, cancellationToken);
                break;
            case ImageOutputFormat.WebP:
                await image.SaveAsWebpAsync(stream, new WebpEncoder { Quality = quality }, cancellationToken);
                break;
            default:
                await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = quality }, cancellationToken);
                break;
        }

        return stream.ToArray();
    }
}
