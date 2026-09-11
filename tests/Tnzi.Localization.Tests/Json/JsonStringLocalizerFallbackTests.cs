using System.Globalization;

namespace Tnzi.Localization.Tests.Json;

/// <summary>
/// JSON 模式的翻译查找链。
///
/// ★ 查找链此前只有两级（当前文化 → 父文化），<c>LocalizationOptions.DefaultCulture</c>
/// 从未被读过。于是一个只写在 <c>en.json</c> 里的键，在 <c>zh-CN</c> 请求下返回的是**键名本身**
/// —— 界面上直接显示 <c>Auth.Login.Title</c> 这种东西。Resx 模式有中性资源兜底，
/// 同一套配置换个 <c>ResourceFormat</c> 就更弱，而这一点在配置上完全看不出来。
/// </summary>
public sealed class JsonStringLocalizerFallbackTests : IDisposable
{
    private readonly string _resources;

    public JsonStringLocalizerFallbackTests()
    {
        _resources = Path.Combine(Path.GetTempPath(), "tnzi-l10n-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_resources);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_resources)) Directory.Delete(_resources, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不该让测试失败
        }
    }

    private void WriteResource(string fileName, string json) =>
        File.WriteAllText(Path.Combine(_resources, fileName), json);

    private JsonStringLocalizer Localizer(string? defaultCulture = "en") =>
        new("SharedResource", _resources, NullLoggerFactory.Instance, null, defaultCulture);

    private static IDisposable UseCulture(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        return new CultureScope(previous);
    }

    private sealed class CultureScope(CultureInfo previous) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }

    /// <summary>★ 核心：只在默认语言里有的键，别的语言也要拿到译文而不是键名。</summary>
    [Fact]
    public void FallsBackToTheDefaultCulture_WhenTheKeyIsMissingEverywhereElse()
    {
        WriteResource("en.json", """{ "Auth": { "Login": "Sign in" } }""");
        WriteResource("zh-CN.json", """{ "Auth": { "Logout": "退出" } }""");
        using var _ = UseCulture("zh-CN");

        var value = Localizer()["Auth.Login"];

        value.Value.ShouldBe("Sign in");
        value.ResourceNotFound.ShouldBeFalse();
    }

    [Fact]
    public void PrefersTheRequestedCultureOverTheDefault()
    {
        WriteResource("en.json", """{ "Auth": { "Login": "Sign in" } }""");
        WriteResource("zh-CN.json", """{ "Auth": { "Login": "登录" } }""");
        using var _ = UseCulture("zh-CN");

        Localizer()["Auth.Login"].Value.ShouldBe("登录");
    }

    /// <summary>
    /// 父文化仍然排在默认语言之前：zh 比 en 更接近 zh-CN。
    ///
    /// ★ 这一条同时钉住"要走**整条**父链"：zh-CN 的父是 zh-Hans 而不是 zh
    /// （ICU 的层级里夹着脚本一层）。只上一级的实现在这里会跳过 zh.json 直接
    /// 落到默认语言，而在 fr-FR 这种父就是 fr 的语言上一切正常 ——
    /// 缺陷只在部分语言上现形。
    /// </summary>
    [Fact]
    public void PrefersTheParentCultureOverTheDefault()
    {
        WriteResource("en.json", """{ "Greeting": "Hello" } """);
        WriteResource("zh.json", """{ "Greeting": "你好" }""");
        using var _ = UseCulture("zh-CN");

        Localizer()["Greeting"].Value.ShouldBe("你好");
    }

    [Fact]
    public void StillReportsAMissingKeyWhenNoCultureHasIt()
    {
        WriteResource("en.json", """{ "Greeting": "Hello" }""");
        using var _ = UseCulture("zh-CN");

        var value = Localizer()["Nope.Missing"];

        value.Value.ShouldBe("Nope.Missing");
        value.ResourceNotFound.ShouldBeTrue();
    }

    /// <summary>
    /// 只有到最后一级都没找到才算缺失 —— 默认语言兜住的键不该被记成缺翻译，
    /// 否则缺失报告会被本来就只写一次的键刷满。
    /// </summary>
    [Fact]
    public void KeysCoveredByTheDefaultCultureAreNotTrackedAsMissing()
    {
        WriteResource("en.json", """{ "Greeting": "Hello" }""");
        var tracker = new Mock<IMissingTranslationTracker>();
        using var _ = UseCulture("zh-CN");

        var localizer = new JsonStringLocalizer("SharedResource", _resources, NullLoggerFactory.Instance, tracker.Object, "en");
        localizer["Greeting"].Value.ShouldBe("Hello");

        tracker.Verify(t => t.TrackMissing(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void TracksTheMissForAKeyNoCultureDefines()
    {
        WriteResource("en.json", """{ "Greeting": "Hello" }""");
        var tracker = new Mock<IMissingTranslationTracker>();
        using var _ = UseCulture("zh-CN");

        var localizer = new JsonStringLocalizer("SharedResource", _resources, NullLoggerFactory.Instance, tracker.Object, "en");
        localizer["Nope"].Value.ShouldBe("Nope");

        tracker.Verify(t => t.TrackMissing("zh-CN", "Nope"), Times.Once);
    }

    [Fact]
    public void FormattedLookupFallsBackTheSameWay()
    {
        WriteResource("en.json", """{ "Welcome": "Welcome, {0}" }""");
        using var _ = UseCulture("zh-CN");

        Localizer()["Welcome", "Ada"].Value.ShouldBe("Welcome, Ada");
    }

    /// <summary>
    /// 资源包（<c>GetAllStrings</c>，前端整包拉走的那份）同样要含默认语言兜住的键，
    /// 否则界面上是半张翻译好的、半张显示键名的表单。
    /// </summary>
    [Fact]
    public void GetAllStrings_IncludesTheKeysOnlyTheDefaultCultureHas()
    {
        WriteResource("en.json", """{ "Only": { "InEnglish": "English only" }, "Shared": "en" }""");
        WriteResource("zh-CN.json", """{ "Shared": "中文" }""");
        using var _ = UseCulture("zh-CN");

        var all = Localizer().GetAllStrings(includeParentCultures: true).ToDictionary(s => s.Name, s => s.Value);

        all["Shared"].ShouldBe("中文");
        all["Only.InEnglish"].ShouldBe("English only");
    }

    [Fact]
    public void GetAllStrings_StaysWithinTheRequestedCultureWhenParentsAreExcluded()
    {
        WriteResource("en.json", """{ "OnlyEnglish": "English only" }""");
        WriteResource("zh-CN.json", """{ "Shared": "中文" }""");
        using var _ = UseCulture("zh-CN");

        var all = Localizer().GetAllStrings(includeParentCultures: false).ToDictionary(s => s.Name, s => s.Value);

        all.ShouldContainKey("Shared");
        all.ShouldNotContainKey("OnlyEnglish");
    }

    /// <summary>
    /// 请求的就是默认语言时不要因为"回退到自己"而重复枚举。
    /// </summary>
    [Fact]
    public void NoDuplicateEntries_WhenTheRequestedCultureIsTheDefault()
    {
        WriteResource("en.json", """{ "Greeting": "Hello" }""");
        using var _ = UseCulture("en");

        var all = Localizer().GetAllStrings(includeParentCultures: true).ToList();

        all.Count(s => s.Name == "Greeting").ShouldBe(1);
    }

    /// <summary>没配默认语言时只走请求文化自己的父链，与本改动前一致。</summary>
    [Fact]
    public void NoDefaultCultureConfigured_StaysWithinTheRequestedCultureChain()
    {
        WriteResource("en.json", """{ "Greeting": "Hello" }""");
        using var _ = UseCulture("zh-CN");

        Localizer(defaultCulture: null)["Greeting"].ResourceNotFound.ShouldBeTrue();
    }
}
