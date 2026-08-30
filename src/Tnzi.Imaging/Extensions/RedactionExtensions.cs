namespace Tnzi.Imaging.Extensions;

/// <summary>
/// 遮挡与模糊：把画面里指向某个人的那一块处理掉。
/// </summary>
/// <remarks>
/// <para>
/// 与本包其余扩展（缩放、水印、缩略图）不同，这里两个方法<b>不是同一类操作</b>：
/// <see cref="Redact(Image{Rgba32}, Rectangle, Color?)"/> 抹掉信息，
/// <see cref="BlurRegion(Image{Rgba32}, Rectangle, float)"/> 只是让它不那么扎眼。
/// 把后者当成前者用，是这一带最常见也最贵的错误 —— 详见各自的说明。
/// </para>
/// </remarks>
public static class RedactionExtensions
{
    /// <summary>遮挡块的默认填充色。</summary>
    private static readonly Color DefaultRedactionColor = Color.Black;

    /// <summary>
    /// 用不透明实色覆盖一块区域，<b>被覆盖的像素不可恢复</b>。
    /// </summary>
    /// <param name="image">图片。</param>
    /// <param name="region">要遮挡的像素矩形。</param>
    /// <param name="fill">填充色；默认黑色。</param>
    /// <returns>处理后的新图（不修改入参）。</returns>
    /// <remarks>
    /// <para>
    /// 直接改写像素，<b>不是叠一层不透明矩形</b>。位图上两者看起来一样，
    /// 但只要产物还会被再处理一道（转格式、抽图层、被别的工具打开），叠加层就可能被剥掉，
    /// 而被遮的内容一直在那儿。这个区别在 PDF 与 SVG 上更致命，位图上按同一条纪律做。
    /// </para>
    /// <para>
    /// 区域超出图像边界会被裁到边界内；完全落在图外则原样返回。
    /// </para>
    /// </remarks>
    public static Image<Rgba32> Redact(this Image<Rgba32> image, Rectangle region, Color? fill = null)
        => image.Redact([region], fill);

    /// <summary>
    /// 一次遮挡多块区域。
    /// </summary>
    /// <param name="image">图片。</param>
    /// <param name="regions">要遮挡的像素矩形集合。</param>
    /// <param name="fill">填充色；默认黑色。</param>
    /// <returns>处理后的新图（不修改入参）。</returns>
    public static Image<Rgba32> Redact(this Image<Rgba32> image, IEnumerable<Rectangle> regions, Color? fill = null)
    {
        Check.NotNull(image);
        Check.NotNull(regions);

        var pixel = (fill ?? DefaultRedactionColor).ToPixel<Rgba32>();
        var clone = image.Clone();

        foreach (var region in regions)
        {
            var clipped = Clip(region, clone.Width, clone.Height);
            if (clipped.Width <= 0 || clipped.Height <= 0)
            {
                continue;
            }

            clone.ProcessPixelRows(accessor =>
            {
                for (var y = clipped.Top; y < clipped.Bottom; y++)
                {
                    accessor.GetRowSpan(y).Slice(clipped.Left, clipped.Width).Fill(pixel);
                }
            });
        }

        return clone;
    }

    /// <summary>
    /// 高斯模糊一块区域。
    /// </summary>
    /// <param name="image">图片。</param>
    /// <param name="region">要模糊的像素矩形。</param>
    /// <param name="sigma">模糊强度，越大越糊；默认 12。</param>
    /// <returns>处理后的新图（不修改入参）。</returns>
    /// <remarks>
    /// ★ <b>模糊不是遮挡。</b>取值空间有限的内容（文字、车牌、门牌号）可以被还原：
    /// 枚举候选、按同样参数模糊一遍、比对哪个最接近就行。公开案例里这么做成功过不止一次。
    /// <para>
    /// 它的正当用途是<b>降低意外暴露</b> —— 先给一张糊的首屏，由查看者主动确认后再完整渲染。
    /// 那保护的是查看者的眼睛，不是画面里的人。要保护画面里的人，用
    /// <see cref="Redact(Image{Rgba32}, Rectangle, Color?)"/>。
    /// </para>
    /// </remarks>
    public static Image<Rgba32> BlurRegion(this Image<Rgba32> image, Rectangle region, float sigma = 12f)
    {
        Check.NotNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sigma);

        var clipped = Clip(region, image.Width, image.Height);
        var clone = image.Clone();
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return clone;
        }

        // ★ 卷积核不能比选区大，否则采样映射直接越界抛异常（sigma 12 落在 20×20 的选区上就会）。
        //   核半径 = ceil(3σ)，所以选区能支撑的最大 sigma 是 floor((边长-1)/2)/3。
        //   夹住而不是抛：调用方给的 sigma 是「想要多糊」，小选区上本来也糊不到那个程度。
        var maxRadius = (Math.Min(clipped.Width, clipped.Height) - 1) / 2;
        var maxSigma = maxRadius / 3f;

        if (maxSigma < 0.5f)
        {
            // 选区小到任何卷积都放不下（每边不足 4 像素）。这时用选区均色平涂：
            // 效果与「糊到看不出」一致，而且不会静默什么都不做 —— 安静跳过等于以为遮住了其实没有。
            return FillWithAverage(clone, clipped);
        }

        clone.Mutate(ctx => ctx.GaussianBlur(Math.Min(sigma, maxSigma), clipped));
        return clone;
    }

    /// <summary>
    /// 用区域自身的平均色平涂该区域。小到无法卷积的选区走这条路。
    /// </summary>
    private static Image<Rgba32> FillWithAverage(Image<Rgba32> image, Rectangle region)
    {
        long r = 0, g = 0, b = 0, a = 0;
        var count = (long)region.Width * region.Height;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = region.Top; y < region.Bottom; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = region.Left; x < region.Right; x++)
                {
                    var pixel = row[x];
                    r += pixel.R;
                    g += pixel.G;
                    b += pixel.B;
                    a += pixel.A;
                }
            }
        });

        var average = new Rgba32((byte)(r / count), (byte)(g / count), (byte)(b / count), (byte)(a / count));

        image.ProcessPixelRows(accessor =>
        {
            for (var y = region.Top; y < region.Bottom; y++)
            {
                accessor.GetRowSpan(y).Slice(region.Left, region.Width).Fill(average);
            }
        });

        return image;
    }

    /// <summary>
    /// 高斯模糊整幅图（模糊化首屏用）。
    /// </summary>
    /// <param name="image">图片。</param>
    /// <param name="sigma">模糊强度，越大越糊；默认 12。</param>
    /// <returns>处理后的新图（不修改入参）。</returns>
    public static Image<Rgba32> Blur(this Image<Rgba32> image, float sigma = 12f)
    {
        Check.NotNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sigma);

        var clone = image.Clone();
        clone.Mutate(ctx => ctx.GaussianBlur(sigma));
        return clone;
    }

    /// <summary>
    /// 把矩形裁到图像边界内。越界的矩形一律裁掉而不是抛异常：
    /// 框选来自界面上的一次拖拽，边缘差一两个像素是常态。
    /// </summary>
    private static Rectangle Clip(Rectangle region, int width, int height)
    {
        var left = Math.Max(0, region.Left);
        var top = Math.Max(0, region.Top);
        var right = Math.Min(width, region.Right);
        var bottom = Math.Min(height, region.Bottom);

        return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
