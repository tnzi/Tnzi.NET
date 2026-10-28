using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// 遮挡对多帧输入同样不可恢复：产物里不存在任何一帧还留着被遮的内容。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：遮挡只改写首帧的像素，而解码保留了全部帧；源图是动画 WebP
/// （或多帧 GIF 配 <c>OutputFormat = Png</c>）时，编码器把其余帧原样写成动画：
/// 首帧上的敏感区域被涂黑，第二帧起在浏览器里照常播放出来。
/// </remarks>
public class MultiFrameRedactionTests
{
    private static readonly Rgba32 Black = new(0, 0, 0, 255);

    private readonly ImageEditor _editor = new();

    [Fact]
    public async Task EditAsync_AnAnimatedWebp_LeavesNoFrameUnredacted()
    {
        var source = MultiFrameSamples.Encode(40, 40, frames: 4, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });

        var result = await _editor.EditAsync(source, "clip.webp", new ImageEditRequest
        {
            Redactions = [new NormalizedRect(0, 0, 0.5, 0.5)]
        });

        result.Format.ShouldBe(ImageOutputFormat.WebP);
        AssertEveryFrameIsBlackAt(result.Content, 5, 5);
    }

    [Fact]
    public async Task EditAsync_AMultiFrameGifWrittenAsPng_LeavesNoFrameUnredacted()
    {
        var source = MultiFrameSamples.Encode(40, 40, frames: 4, new GifEncoder());

        var result = await _editor.EditAsync(source, "clip.gif", new ImageEditRequest
        {
            Redactions = [new NormalizedRect(0, 0, 0.5, 0.5)],
            OutputFormat = ImageOutputFormat.Png
        });

        AssertEveryFrameIsBlackAt(result.Content, 5, 5);
    }

    /// <summary>
    /// 编辑器只交出首帧：遮挡框是在一张静止画面上画的，后面各帧里同一块内容可能挪了位置，
    /// 把同一个框套到每一帧上并不保证遮住了它。产物与调用方看到并框选的那一帧一致。
    /// </summary>
    [Fact]
    public async Task EditAsync_AnAnimatedSource_YieldsTheFirstFrameAsAStill()
    {
        var source = MultiFrameSamples.Encode(40, 40, frames: 4, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });

        var result = await _editor.EditAsync(source, "clip.webp", new ImageEditRequest { MaxWidth = 20 });

        using var output = Image.Load<Rgba32>(result.Content);
        output.Frames.Count.ShouldBe(1);

        // 输出按 Quality 有损编码，颜色差几个单位；要证明的只是「这是首帧」
        var pixel = output[10, 10];
        var first = MultiFrameSamples.FrameColor(0);
        Math.Abs(pixel.R - first.R).ShouldBeLessThan(8);
        Math.Abs(pixel.B - first.B).ShouldBeLessThan(8);
    }

    /// <summary>
    /// 公开的扩展方法本身也覆盖每一帧：消费方直接拿一张多帧图调用它时，承诺的「不可恢复」照样成立。
    /// </summary>
    [Fact]
    public void Redact_OverwritesTheRegionOnEveryFrame()
    {
        using var image = MultiFrameSamples.Create(20, 20, frames: 3);

        using var redacted = image.Redact(new Rectangle(0, 0, 10, 10));

        redacted.Frames.Count.ShouldBe(3);
        for (var i = 0; i < redacted.Frames.Count; i++)
        {
            redacted.Frames[i][5, 5].ShouldBe(Black, $"frame {i} inside the region");
            redacted.Frames[i][15, 15].ShouldBe(MultiFrameSamples.FrameColor(i), $"frame {i} outside the region");
        }
    }

    /// <summary>
    /// 小到放不下卷积核的选区走均色平涂；那条路同样要覆盖每一帧，且每帧按自己的均色涂。
    /// </summary>
    [Fact]
    public void BlurRegion_ATinyRegion_IsFlattenedOnEveryFrame()
    {
        using var image = MultiFrameSamples.Create(20, 20, frames: 3);
        for (var i = 0; i < image.Frames.Count; i++)
        {
            // 每帧在选区里放一个异色点：平涂后它必须消失
            image.Frames[i][1, 1] = Black;
        }

        using var blurred = image.BlurRegion(new Rectangle(0, 0, 3, 3));

        for (var i = 0; i < blurred.Frames.Count; i++)
        {
            var frame = blurred.Frames[i];
            frame[1, 1].ShouldBe(frame[0, 0], $"frame {i} is uniform inside the region");
            frame[1, 1].ShouldNotBe(Black, $"frame {i} no longer shows the odd pixel");
        }
    }

    private static void AssertEveryFrameIsBlackAt(byte[] content, int x, int y)
    {
        using var output = Image.Load<Rgba32>(content);
        for (var i = 0; i < output.Frames.Count; i++)
        {
            output.Frames[i][x, y].ShouldBe(Black, $"frame {i} of {output.Frames.Count}");
        }
    }
}
