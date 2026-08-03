namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="ImagePayload"/> 的签名图载荷解码。
/// </summary>
/// <remarks>前端签名板给出来的形态不统一（data URL / 裸 base64 / 折行过的 base64），三种都要认。</remarks>
public class ImagePayloadTests
{
    [Fact]
    public void RawContent_TakesPrecedenceOverTheDataUrl()
    {
        var content = TestImages.Png(2, 2);
        var stamp = new PdfImageStamp { Content = content, DataUrl = "data:image/png;base64,AAAA" };

        ImagePayload.TryResolve(stamp, out var bytes).ShouldBeTrue();
        bytes.ShouldBe(content);
    }

    [Fact]
    public void DataUrl_IsDecoded()
    {
        var content = TestImages.Png(2, 2);
        var stamp = new PdfImageStamp { DataUrl = "data:image/png;base64," + Convert.ToBase64String(content) };

        ImagePayload.TryResolve(stamp, out var bytes).ShouldBeTrue();
        bytes.ShouldBe(content);
    }

    [Fact]
    public void BareBase64_IsAlsoAccepted()
    {
        var content = TestImages.Png(2, 2);

        ImagePayload.TryDecode(Convert.ToBase64String(content), out var bytes).ShouldBeTrue();
        bytes.ShouldBe(content);
    }

    [Fact]
    public void WrappedBase64_IsAccepted()
    {
        var content = TestImages.Png(2, 2);
        var wrapped = string.Join("\r\n", Chunk(Convert.ToBase64String(content), 24));

        ImagePayload.TryDecode(wrapped, out var bytes).ShouldBeTrue();
        bytes.ShouldBe(content);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("data:image/png,not-base64-at-all")]
    [InlineData("!!! not base64 !!!")]
    public void UndecodableInputs_ReturnFalse(string? value)
    {
        ImagePayload.TryDecode(value, out var bytes).ShouldBeFalse();
        bytes.ShouldBeEmpty();
    }

    [Fact]
    public void EmptyContent_FallsBackToTheDataUrl()
    {
        var content = TestImages.Png(2, 2);
        var stamp = new PdfImageStamp { Content = [], DataUrl = Convert.ToBase64String(content) };

        ImagePayload.TryResolve(stamp, out var bytes).ShouldBeTrue();
        bytes.ShouldBe(content);
    }

    private static IEnumerable<string> Chunk(string value, int size)
    {
        for (var index = 0; index < value.Length; index += size)
            yield return value.Substring(index, Math.Min(size, value.Length - index));
    }
}
