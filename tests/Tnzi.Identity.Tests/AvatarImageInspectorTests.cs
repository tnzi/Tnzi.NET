using Tnzi.Identity.Extensions;

namespace Tnzi.Identity.Tests;

public class AvatarImageInspectorTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1];
    private static readonly byte[] Webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();

    [Fact]
    public void DetectFormat_RecognisesTheThreeAllowedFormats()
    {
        AvatarImageInspector.DetectFormat(Png).ShouldBe("png");
        AvatarImageInspector.DetectFormat(Jpeg).ShouldBe("jpeg");
        AvatarImageInspector.DetectFormat(Webp).ShouldBe("webp");
    }

    /// <summary>SVG 是会执行脚本的文档，GIF 不在允许清单里，RIFF 的其它子格式（WAV/AVI）不是 WebP。</summary>
    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\">")]
    [InlineData("GIF89a......")]
    [InlineData("RIFF\0\0\0\0WAVEfmt ")]
    [InlineData("%PDF-1.7\n....")]
    [InlineData("")]
    public void DetectFormat_RejectsEverythingElse(string content)
    {
        AvatarImageInspector.DetectFormat(System.Text.Encoding.ASCII.GetBytes(content)).ShouldBeNull();
    }

    [Fact]
    public async Task InspectAsync_SeekableStream_UsesItsLength()
    {
        var bytes = Png.Concat(new byte[100]).ToArray();

        var (format, tooLarge) = await AvatarImageInspector.InspectAsync(new MemoryStream(bytes), maxBytes: bytes.Length);
        format.ShouldBe("png");
        tooLarge.ShouldBeFalse();

        (await AvatarImageInspector.InspectAsync(new MemoryStream(bytes), maxBytes: bytes.Length - 1)).TooLarge.ShouldBeTrue();
    }

    /// <summary>对象存储的下载流不可寻址：边读边数，结果与可寻址时一致。</summary>
    [Fact]
    public async Task InspectAsync_NonSeekableStream_CountsTheBytes()
    {
        var bytes = Jpeg.Concat(new byte[200_000]).ToArray();

        var within = await AvatarImageInspector.InspectAsync(new ForwardOnlyStream(bytes), maxBytes: bytes.Length);
        within.Format.ShouldBe("jpeg");
        within.TooLarge.ShouldBeFalse();

        (await AvatarImageInspector.InspectAsync(new ForwardOnlyStream(bytes), maxBytes: bytes.Length - 1)).TooLarge.ShouldBeTrue();
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }
}
