using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Tnzi.Localization.Controllers;
using Tnzi.Localization.Resources;
using Tnzi.Modules;

namespace Tnzi.Localization.Tests.Resx;

/// <summary>
/// 缺失翻译追踪必须在默认的 Resx 模式下也接线。
///
/// ★ 此前 <c>TrackMissing</c> 全仓唯一的调用点在 <c>JsonStringLocalizer</c>，而 <c>ResourceFormat</c> 默认是 Resx：
/// 追踪器无条件注册、4 个管理端点 + 2 个权限码 + 管理页面都在，可默认部署上永远收不到任何键 ——
/// 「Missing Translations」页恒为空、summary 全零，与「所有翻译都齐了」逐字相同，而界面上正显示着键名。
/// </summary>
public sealed class ResxMissingTranslationTrackingTests
{
    private static ServiceProvider BuildProvider(string resourceFormat)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Localization:ResourceFormat"] = resourceFormat,
                ["Localization:SupportedCultures:0"] = "en",
                ["Localization:SupportedCultures:1"] = "zh-CN",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(Environments.Production);
        services.AddSingleton(environment.Object);

        var module = new LocalizationModule();
        var context = new ServiceConfigurationContext(services, configuration);
        module.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();

        return services.BuildServiceProvider();
    }

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

    [Fact]
    public async Task ResxMode_UnresolvedKey_IsTracked()
    {
        using var provider = BuildProvider("Resx");
        var localizer = provider.GetRequiredService<IStringLocalizer<ResxProbeResource>>();
        var tracker = provider.GetRequiredService<IMissingTranslationTracker>();
        using var culture = UseCulture("zh-CN");

        var value = localizer["Auth.Login.Title"];

        value.ResourceNotFound.ShouldBeTrue();
        value.Value.ShouldBe("Auth.Login.Title");
        var missing = await tracker.GetMissingKeysAsync();
        missing.ShouldContain(m => m.Key == "Auth.Login.Title" && m.Culture == "zh-CN");
    }

    [Fact]
    public async Task ResxMode_FormattedLookup_IsTrackedToo()
    {
        using var provider = BuildProvider("Resx");
        var localizer = provider.GetRequiredService<IStringLocalizer<ResxProbeResource>>();
        var tracker = provider.GetRequiredService<IMissingTranslationTracker>();
        using var culture = UseCulture("en");

        _ = localizer["Validation.Phone.Required", "Phone"];

        (await tracker.GetMissingKeysAsync()).ShouldContain(m => m.Key == "Validation.Phone.Required");
    }

    /// <summary>
    /// ★★ 一个资源都没有的资源源，它的未命中不记：那不是「缺一条翻译」，而是这个键在 Resx 模式下根本无处可补。
    /// </summary>
    /// <remarks>
    /// Resx 只在资源类型<b>自己的程序集</b>里找清单。框架的 <see cref="SharedResource"/> 住在 Tnzi.Localization，
    /// 那里没有任何 .resx；消费方照文档在自己项目里加的 SharedResource.resx 编进的是消费方程序集，永远不会被查。
    /// 此前每个异常都经它查一次错误码，Missing Translations 页因此堆满 NOT_FOUND、VALIDATION_ERROR 这类
    /// 运维无论如何也补不上的键。
    /// </remarks>
    [Fact]
    public async Task ResxMode_KeysOfASourceWithNoResourcesInAnySupportedCulture_AreNotTracked()
    {
        using var provider = BuildProvider("Resx");
        var localizer = provider.GetRequiredService<IStringLocalizer<SharedResource>>();
        var tracker = provider.GetRequiredService<IMissingTranslationTracker>();
        using var culture = UseCulture("zh-CN");

        var value = localizer["NOT_FOUND"];

        value.ResourceNotFound.ShouldBeTrue();
        (await tracker.GetMissingKeysAsync()).ShouldBeEmpty();
    }

    /// <summary>
    /// ★ 匿名的 <c>GET localization/resources/{culture}</c> 在默认 Resx 模式下对每个受支持语言都 500：
    /// 框架的 SharedResource 没有任何清单，<c>GetAllStrings</c> 抛 <c>MissingManifestResourceException</c>。
    /// 没有资源就答一个空集。
    /// </summary>
    [Fact]
    public void ResxMode_ResourcesEndpoint_ReturnsAnEmptySet_InsteadOfThrowing()
    {
        using var provider = BuildProvider("Resx");
        var controller = new DefaultLocalizationController(
            provider.GetRequiredService<IOptions<RequestLocalizationOptions>>(),
            provider.GetRequiredService<IStringLocalizerFactory>());

        var result = controller.GetResources("en");

        result.Success.ShouldBeTrue();
        result.Data!.Culture.ShouldBe("en");
        result.Data.Resources.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResxMode_ResolvedKey_IsNotTracked()
    {
        using var provider = BuildProvider("Resx");
        var tracker = provider.GetRequiredService<IMissingTranslationTracker>();
        var factory = provider.GetRequiredService<IStringLocalizerFactory>();
        using var culture = UseCulture("zh-CN");

        // 本程序集自带的 .resx 资源：命中中性资源的键不是缺翻译
        var localizer = factory.Create(typeof(ResxProbeResource));
        var value = localizer["Probe.Hit"];

        value.ResourceNotFound.ShouldBeFalse();
        value.Value.ShouldBe("hit");
        (await tracker.GetMissingKeysAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ResxMode_GetAllStrings_PassesThrough()
    {
        using var provider = BuildProvider("Resx");
        var factory = provider.GetRequiredService<IStringLocalizerFactory>();
        var tracker = provider.GetRequiredService<IMissingTranslationTracker>();

        var all = factory.Create(typeof(ResxProbeResource)).GetAllStrings(includeParentCultures: true).ToList();

        all.ShouldContain(s => s.Name == "Probe.Hit" && s.Value == "hit");
        (await tracker.GetMissingKeysAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task JsonMode_StillTracks()
    {
        using var provider = BuildProvider("Json");
        var localizer = provider.GetRequiredService<IStringLocalizer<SharedResource>>();
        var tracker = provider.GetRequiredService<IMissingTranslationTracker>();
        using var culture = UseCulture("en");

        _ = localizer["Auth.Login.Title"];

        (await tracker.GetMissingKeysAsync()).ShouldContain(m => m.Key == "Auth.Login.Title");
    }
}
