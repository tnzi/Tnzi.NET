
namespace Tnzi.Imaging.Tests;

public class ImagingOptionsValidatorTests
{
    private readonly ImagingOptionsValidator _validator = new();

    [Fact]
    public void ValidOptions_Succeeds()
    {
        var options = new ImagingOptions();
        var result = _validator.Validate(null, options);
        result.Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// 容差必须有上界，否则「验证」形同虚设。
    /// </summary>
    /// <remarks>
    /// ★ 此前只校验了 <c>Tolerance &lt; 0</c>：把容差配到与拼图块同宽（甚至比整张图还宽），
    /// 校验一声不响地通过，而此后<b>任何</b>滑动位置都算验证成功 ——
    /// 页面上还是那个验证码，日志里还是那些"验证通过"，它只是不再拦任何人。
    /// </remarks>
    [Fact]
    public void SlidingTolerance_AtOrAboveThePieceSize_Fails()
    {
        var options = new ImagingOptions { SlidingCaptcha = { PieceSize = 50, Tolerance = 50 } };

        _validator.Validate(null, options).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void SlidingTolerance_WiderThanTheWholeImage_Fails()
    {
        var options = new ImagingOptions { SlidingCaptcha = { Width = 300, Tolerance = 400 } };

        _validator.Validate(null, options).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void SlidingTolerance_BelowThePieceSize_Succeeds()
    {
        var options = new ImagingOptions { SlidingCaptcha = { PieceSize = 50, Tolerance = 8 } };

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void MaxDecodePixels_Negative_Fails()
    {
        var options = new ImagingOptions { MaxDecodePixels = -1 };

        _validator.Validate(null, options).Succeeded.ShouldBeFalse();
    }

    /// <summary>0 是「不限制」的显式写法，不是配置错误。</summary>
    [Fact]
    public void MaxDecodePixels_Zero_Succeeds()
    {
        var options = new ImagingOptions { MaxDecodePixels = 0 };

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void FontSize_TooSmall_Fails()
    {
        var options = new ImagingOptions { Captcha = { FontSize = 5 } };
        var result = _validator.Validate(null, options);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void FontSize_TooLarge_Fails()
    {
        var options = new ImagingOptions { Captcha = { FontSize = 200 } };
        var result = _validator.Validate(null, options);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Height_Negative_Fails()
    {
        var options = new ImagingOptions { Captcha = { Height = -1 } };
        var result = _validator.Validate(null, options);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void RandomPointPercent_OutOfRange_Fails()
    {
        var options = new ImagingOptions { Captcha = { RandomPointPercent = 101 } };
        var result = _validator.Validate(null, options);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void RandomLineCount_Negative_Fails()
    {
        var options = new ImagingOptions { Captcha = { RandomLineCount = -1 } };
        var result = _validator.Validate(null, options);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void ExpireMinutes_TooSmall_Fails()
    {
        var options = new ImagingOptions { Captcha = { ExpireMinutes = 0 } };
        var result = _validator.Validate(null, options);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void ExpireMinutes_TooLarge_Fails()
    {
        var options = new ImagingOptions { Captcha = { ExpireMinutes = 100 } };
        var result = _validator.Validate(null, options);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void DefaultLength_TooSmall_Fails()
    {
        var options = new ImagingOptions { Captcha = { DefaultLength = 1 } };
        var result = _validator.Validate(null, options);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void DefaultLength_TooLarge_Fails()
    {
        var options = new ImagingOptions { Captcha = { DefaultLength = 15 } };
        var result = _validator.Validate(null, options);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void DefaultOptions_AllValid()
    {
        var options = new ImagingOptions();
        var captcha = options.Captcha;

        captcha.FontSize.ShouldBe(20);
        captcha.RandomColor.ShouldBeTrue();
        captcha.RandomLineCount.ShouldBe(2);
        captcha.ExpireMinutes.ShouldBe(5);
        captcha.DefaultLength.ShouldBe(4);
    }
}
