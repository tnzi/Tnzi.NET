using SixLabors.ImageSharp.Formats;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// 多帧样本：每帧颜色各不相同的动画，以及逐字节拼出来的「大画布 + 极小帧」GIF。
/// </summary>
internal static class MultiFrameSamples
{
    /// <summary>
    /// 一张 <paramref name="frames"/> 帧的图，第 i 帧整幅填 <see cref="FrameColor"/>(i)。
    /// </summary>
    public static Image<Rgba32> Create(int width, int height, int frames)
    {
        var image = new Image<Rgba32>(width, height, FrameColor(0));
        for (var i = 1; i < frames; i++)
        {
            using var frame = new Image<Rgba32>(width, height, FrameColor(i));
            image.Frames.AddFrame(frame.Frames.RootFrame);
        }

        return image;
    }

    /// <summary>按指定编码器编码 <see cref="Create"/> 的产物。</summary>
    public static byte[] Encode(int width, int height, int frames, IImageEncoder encoder)
    {
        using var image = Create(width, height, frames);
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>
    /// 每帧一个颜色，且都不是黑色（遮挡的默认填充色），好断言「这一帧被遮住了没有」。
    /// </summary>
    public static Rgba32 FrameColor(int index) => new((byte)(40 + index * 40 % 200), 200, (byte)(255 - index * 30 % 200));

    /// <summary>
    /// 攻击形态：画布声明 <paramref name="width"/>×<paramref name="height"/>，随后是 <paramref name="frames"/> 个
    /// 1×1 的子图。每帧只占十几个字节，而解码器给每一帧都分配一整张画布。
    /// </summary>
    /// <remarks>
    /// 逐字节拼而不用 <c>GifEncoder</c>：编码器会把每帧写成整幅画布的数据，样本就不再是「文件极小、声明极大」那个形状。
    /// </remarks>
    public static byte[] GifWithTinyFrames(int width, int height, int frames)
    {
        var gif = new List<byte>();
        gif.AddRange("GIF89a"u8.ToArray());

        // 逻辑屏幕描述符：宽、高（小端），带一张 2 色的全局色表
        gif.AddRange(LittleEndian16(width));
        gif.AddRange(LittleEndian16(height));
        gif.Add(0x80);  // 有全局色表，色表大小 2^(0+1) = 2
        gif.Add(0x00);  // 背景色索引
        gif.Add(0x00);  // 像素宽高比
        gif.AddRange([0x10, 0x20, 0x30, 0xF0, 0xE0, 0xD0]);

        for (var i = 0; i < frames; i++)
        {
            // 图像描述符：左上角 (0,0)，1×1，无局部色表
            gif.Add(0x2C);
            gif.AddRange(LittleEndian16(0));
            gif.AddRange(LittleEndian16(0));
            gif.AddRange(LittleEndian16(1));
            gif.AddRange(LittleEndian16(1));
            gif.Add(0x00);

            // LZW：最小码长 2；码流为 清除(4)、像素 0、结束(5)，每码 3 位、低位在前打包成 0x44 0x01
            gif.Add(0x02);
            gif.AddRange([0x02, 0x44, 0x01, 0x00]);
        }

        gif.Add(0x3B);
        return [.. gif];
    }

    private static byte[] LittleEndian16(int value) => [(byte)value, (byte)(value >> 8)];
}
