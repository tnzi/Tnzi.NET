namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="PdfRasterOptionsValidator"/> 的行为。
/// </summary>
public class PdfRasterOptionsValidatorTests
{
    private static string? Validate(long maxPagePixels)
    {
        var result = new PdfRasterOptionsValidator()
            .Validate(name: null, new PdfRasterOptions { MaxPagePixels = maxPagePixels });

        return result.Failed ? result.FailureMessage : null;
    }

    [Fact]
    public void TheDefault_IsValid() => Validate(PdfRasterOptions.DefaultMaxPagePixels).ShouldBeNull();

    [Fact]
    public void Zero_MeansNoLimitAndIsValid() => Validate(0).ShouldBeNull();

    [Fact]
    public void ANegativeLimit_IsRejected() => Validate(-1).ShouldNotBeNull();

    [Fact]
    public void ALimitTooSmallToRenderAnything_IsRejected()
    {
        // ★ 这不是「更严格」，是把整个能力配没了 —— 而症状会是每一次渲染都失败，
        // 谁也不会想到去看这一项配置。启动期拦下来。
        Validate(1_000).ShouldNotBeNull();
    }
}
