using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Localization;
using Tnzi.Localization.Controllers;

namespace Tnzi.Localization.Tests.Controllers;

/// <summary>
/// <c>GET localization/resources/{culture}</c> 的入参校验。
///
/// ★ 这个端点是 <c>[AllowAnonymous]</c> 的，而 culture 段此前只交给
/// <see cref="CultureInfo"/> 构造 —— .NET 会为几乎任意字符串造出一个"自定义文化"
/// 而不抛异常。于是每个没见过的名字都在单例工厂的 <c>GetOrAdd</c> 里留下一条
/// 永不回收的缓存项，外加两次 <c>File.Exists</c>：一条未认证的、缓慢的内存增长路径。
///
/// 同样重要的是答案本身：对任意串都回 200 + 空集，调用方分不清"这个语言没有翻译"
/// 和"根本没有这个语言"。
/// </summary>
public class LocalizationResourcesTests
{
    private static DefaultLocalizationController Create(params string[] supported)
    {
        var options = new RequestLocalizationOptions()
            .SetDefaultCulture(supported.Length > 0 ? supported[0] : "en")
            .AddSupportedCultures(supported)
            .AddSupportedUICultures(supported);

        var localizer = new Mock<IStringLocalizer>();
        localizer.Setup(l => l.GetAllStrings(It.IsAny<bool>()))
            .Returns([new LocalizedString("Greeting", "Hello")]);

        var factory = new Mock<IStringLocalizerFactory>();
        factory.Setup(f => f.Create(It.IsAny<Type>())).Returns(localizer.Object);

        return new DefaultLocalizationController(Microsoft.Extensions.Options.Options.Create(options), factory.Object);
    }

    [Fact]
    public void ReturnsResourcesForASupportedCulture()
    {
        var result = Create("en", "zh-CN").GetResources("zh-CN");

        result.Success.ShouldBeTrue();
        result.Data!.Culture.ShouldBe("zh-CN");
        result.Data.Resources.ShouldContainKey("Greeting");
    }

    /// <summary>★ 核心：未列入支持语言的名字必须被拒，而不是回一个空集合。</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("x-not-a-language")]
    [InlineData("../../etc/passwd")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void RejectsCulturesThatAreNotSupported(string culture)
    {
        var result = Create("en", "zh-CN").GetResources(culture);

        result.Success.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>
    /// 被拒时不得触碰工厂 —— 缓存增长正是要挡住的那件事。
    /// </summary>
    [Fact]
    public void DoesNotTouchTheLocalizerFactoryForAnUnsupportedCulture()
    {
        var factory = new Mock<IStringLocalizerFactory>();
        var options = new RequestLocalizationOptions()
            .SetDefaultCulture("en")
            .AddSupportedCultures("en")
            .AddSupportedUICultures("en");
        var controller = new DefaultLocalizationController(Microsoft.Extensions.Options.Options.Create(options), factory.Object);

        controller.GetResources("de-DE").Success.ShouldBeFalse();

        factory.Verify(f => f.Create(It.IsAny<Type>()), Times.Never);
    }

    [Fact]
    public void CultureMatchingIsCaseInsensitive()
    {
        Create("en", "zh-CN").GetResources("ZH-cn").Success.ShouldBeTrue();
    }

    [Fact]
    public void RestoresTheAmbientCultureAfterwards()
    {
        var before = CultureInfo.CurrentUICulture;

        Create("en", "zh-CN").GetResources("zh-CN");

        CultureInfo.CurrentUICulture.ShouldBe(before);
    }

    [Fact]
    public void ListsTheSupportedCulturesWithTheDefaultFlagged()
    {
        var result = Create("en", "zh-CN").GetCultures();

        result.Data!.Cultures.Select(c => c.Name).ShouldBe(["en", "zh-CN"]);
        result.Data.Cultures.Single(c => c.Name == "en").IsDefault.ShouldBeTrue();
    }
}
