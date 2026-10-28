using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.Formats.Webp;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// 解码闸门按<b>解码后的总像素</b>判上限：多帧图像的每一帧都是一整张画布。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：闸门只比较单帧的宽×高，解码时却把所有帧都解出来。
/// GIF 的每一帧可以只是一个 1×1 的子图（十几个字节），解码器照样给它分配整张画布：
/// 6000×6000 的画布（低于 4000 万上限）乘以一千帧，是一个几十 KB 的上传换来上百 GB 的分配。
/// 上传后自动生成缩略图意味着任何能上传的人都够得着。
/// </para>
/// <para>
/// 这里的用例都用很小的图配很小的上限，让「按帧数算」与「只算一帧」两种实现给出不同的回答，
/// 而不必真的去解一颗炸弹。
/// </para>
/// </remarks>
public class MultiFrameDecodeGuardTests
{
    public static TheoryData<string> MultiFrameFormats => ["gif", "webp", "tiff"];

    [Theory]
    [MemberData(nameof(MultiFrameFormats))]
    public void EveryFrameCountsAgainstTheLimit(string format)
    {
        // 单帧 100 像素恰好在上限上，五帧就是 500
        var bytes = MultiFrameSamples.Encode(10, 10, frames: 5, EncoderFor(format));

        var ex = Should.Throw<ImageDecodeLimitExceededException>(() => ImageDecodeGuard.Load(bytes, maxPixels: 100));

        ex.Frames.ShouldBe(5);
        ex.Pixels.ShouldBe(500);
    }

    [Theory]
    [MemberData(nameof(MultiFrameFormats))]
    public async Task TheAsyncEntryPoint_CountsEveryFrameToo(string format)
    {
        var bytes = MultiFrameSamples.Encode(10, 10, frames: 5, EncoderFor(format));

        await Should.ThrowAsync<ImageDecodeLimitExceededException>(() => ImageDecodeGuard.LoadAsync(bytes, maxPixels: 100));
    }

    [Fact]
    public async Task ANonSeekableMultiFrameStream_CountsEveryFrameToo()
    {
        using var stream = new NonSeekableStream(MultiFrameSamples.Encode(10, 10, frames: 5, new GifEncoder()));

        await Should.ThrowAsync<ImageDecodeLimitExceededException>(() => ImageDecodeGuard.LoadAsync(stream, maxPixels: 100));
    }

    /// <summary>
    /// 正控制：总像素在上限之内的动画照常解码，而且一帧不少：闸门不是「把动画降成一帧」。
    /// </summary>
    [Theory]
    [MemberData(nameof(MultiFrameFormats))]
    public void AnAnimationWithinTheBudget_KeepsEveryFrame(string format)
    {
        var bytes = MultiFrameSamples.Encode(10, 10, frames: 5, EncoderFor(format));

        using var image = ImageDecodeGuard.Load(bytes, maxPixels: 500);

        image.Frames.Count.ShouldBe(5);
    }

    /// <summary>
    /// 攻击形态本身：6000×6000 的画布、一千个 1×1 的子图，文件只有十几 KB。
    /// 默认上限下必须在解码之前被拒绝；实现若是「先解码再判断」，这条用例会把测试进程一起带走。
    /// </summary>
    [Fact]
    public async Task ATinyGifDeclaringAThousandFullCanvasFrames_IsRejectedBeforeDecoding()
    {
        var bomb = MultiFrameSamples.GifWithTinyFrames(6000, 6000, frames: 1000);
        bomb.Length.ShouldBeLessThan(20_000);

        var ex = await Should.ThrowAsync<ImageDecodeLimitExceededException>(() => ImageDecodeGuard.LoadAsync(bomb));

        ex.Width.ShouldBe(6000);
        ex.Frames.ShouldBe(1000);
        ex.Limit.ShouldBe(ImageDecodeGuard.DefaultMaxDecodePixels);
    }

    /// <summary>
    /// 多页 TIFF 的每一页按它自己声明的尺寸分配：首页 4×4、次页 3000×2000 的文件只有几百字节，
    /// 按画布（首页）计只有 32 像素。ImageSharp 先把每一页都解出来、最后才报「尺寸不一致不支持」，
    /// 那时次页的整张位图已经分配过了。闸门必须在那之前按次页声明的尺寸拒绝。
    /// </summary>
    [Fact]
    public void ATiffPageDeclaringALargerSizeThanTheFirst_CountsAtItsOwnSize()
    {
        var bytes = TiffSamples.ClassicPages(pixelDataLimit: 16, (4, 4), (3000, 2000));
        bytes.Length.ShouldBeLessThan(1_000);

        var ex = Should.Throw<ImageDecodeLimitExceededException>(() => ImageDecodeGuard.Load(bytes, maxPixels: 1_000_000));

        ex.Frames.ShouldBe(2);
        ex.Pixels.ShouldBe(16 + 6_000_000);
    }

    [Fact]
    public async Task LoadFirstFrame_DecodesOnlyTheFirstFrame()
    {
        // 五帧共 500 像素，上限 100：全部解码会被拒绝，只解首帧则恰好在上限上
        var bytes = MultiFrameSamples.Encode(10, 10, frames: 5, new GifEncoder());

        using var image = await ImageDecodeGuard.LoadFirstFrameAsync(bytes, maxPixels: 100);

        image.Frames.Count.ShouldBe(1);
        image[5, 5].ShouldBe(MultiFrameSamples.FrameColor(0));
    }

    /// <summary>
    /// 只解首帧时，攻击样本退化成一张普通的大图：按单张画布判上限，照常解出一帧，
    /// 其余九百九十九帧既不识别也不分配。
    /// </summary>
    [Fact]
    public async Task LoadFirstFrame_OfTheTinyFrameBomb_DecodesASingleCanvas()
    {
        var bomb = MultiFrameSamples.GifWithTinyFrames(600, 600, frames: 1000);

        using var image = await ImageDecodeGuard.LoadFirstFrameAsync(bomb, maxPixels: 600 * 600);

        image.Frames.Count.ShouldBe(1);
        image.Width.ShouldBe(600);
    }

    [Fact]
    public async Task LoadFirstFrame_StillRejectsAFirstFrameAboveTheLimit()
    {
        var bytes = MultiFrameSamples.Encode(10, 10, frames: 2, new GifEncoder());

        await Should.ThrowAsync<ImageDecodeLimitExceededException>(() => ImageDecodeGuard.LoadFirstFrameAsync(bytes, maxPixels: 99));
    }

    /// <summary>
    /// 首页之后那一页声明得再大，只解首帧时都不碰它（ImageSharp 不会走到「尺寸不一致」那一步）。
    /// </summary>
    [Fact]
    public async Task LoadFirstFrame_OfAMixedSizeTiff_ReadsTheFirstPageOnly()
    {
        var bytes = TiffSamples.ClassicPages(pixelDataLimit: 16, (4, 4), (3000, 2000));

        using var image = await ImageDecodeGuard.LoadFirstFrameAsync(bytes, maxPixels: 1_000);

        image.Frames.Count.ShouldBe(1);
        image.Width.ShouldBe(4);
    }

    private static IImageEncoder EncoderFor(string format) => format switch
    {
        "gif" => new GifEncoder(),
        "webp" => new WebpEncoder { FileFormat = WebpFileFormatType.Lossless },
        // 显式不压缩：CCITT 编码器是被压制的告警所在，测试也不去碰它
        "tiff" => new TiffEncoder { Compression = TiffCompression.None },
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };
}
