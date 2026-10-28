using SixLabors.ImageSharp;

namespace Tnzi.Storage.Tests;

/// <summary>
/// 上传后自动生成缩略图只解首帧：动画与多页文件的其余帧既不识别也不解码。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：缩略图在上传请求里解码<b>全部</b>帧，而解码闸门只按单帧的宽×高判上限。
/// GIF 的一帧哪怕只是一个 1×1 的子图（十几个字节），解码器也给它分配整张画布：
/// 6000×6000 的画布配一千帧，是几十 KB 的上传换上百 GB 的分配，任何能上传的人都够得着。
/// </para>
/// <para>
/// 这里用一张 2000×2000、二十帧的样本：单帧 400 万像素，二十帧共 8000 万，超过默认的 4000 万上限。
/// 只解首帧时照常出图；按全部帧解码时，闸门会（正确地）拒绝它，于是没有缩略图。这条用例借此区分两种路径。
/// </para>
/// </remarks>
public class AnimatedThumbnailTests
{
    [Fact]
    public async Task AnAnimatedGif_GetsAThumbnailFromItsFirstFrameAlone()
    {
        var gif = GifWithTinyFrames(2000, 2000, frames: 20);
        var (generator, storage, path, _) = await StoreAsync(gif, ".gif");

        var thumbnail = await generator.GenerateAsync(path, ".gif", size: gif.Length);

        Assert.False(string.IsNullOrEmpty(thumbnail));
        await using var stream = await storage.DownloadAsync(thumbnail!);
        using var image = await Image.LoadAsync(stream);
        Assert.Equal(1, image.Frames.Count);
    }

    private static async Task<(FileThumbnailGenerator Generator, InMemoryFileStorage Storage, string Path, string Key)> StoreAsync(
        byte[] content, string extension)
    {
        var storage = new InMemoryFileStorage();
        var options = new StaticOptionsMonitor<StorageOptions>(new StorageOptions());
        var generator = new FileThumbnailGenerator(storage, options);

        var key = $"uploads/clip{extension}";
        var path = await storage.UploadAsync(key, new MemoryStream(content));
        return (generator, storage, path, key);
    }

    /// <summary>
    /// 逐字节拼的 GIF：画布 <paramref name="width"/>×<paramref name="height"/>，随后 <paramref name="frames"/> 个 1×1 子图。
    /// </summary>
    private static byte[] GifWithTinyFrames(int width, int height, int frames)
    {
        var gif = new List<byte>();
        gif.AddRange("GIF89a"u8.ToArray());
        gif.AddRange([(byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8)]);
        gif.AddRange([0x80, 0x00, 0x00]);                       // 2 色全局色表，背景索引 0，宽高比 0
        gif.AddRange([0x10, 0x20, 0x30, 0xF0, 0xE0, 0xD0]);

        for (var i = 0; i < frames; i++)
        {
            gif.AddRange([0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0x00]);  // 图像描述符：(0,0) 处 1×1，无局部色表
            gif.AddRange([0x02, 0x02, 0x44, 0x01, 0x00]);        // LZW：清除、像素 0、结束
        }

        gif.Add(0x3B);
        return [.. gif];
    }
}
