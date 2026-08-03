
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
