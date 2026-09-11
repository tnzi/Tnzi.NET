
using Microsoft.Extensions.Configuration;
using Tnzi.Options;

namespace Tnzi.Tests.AI;

/// <summary>
/// 核心 provider 注册表的解析与校验。
/// </summary>
/// <remarks>
/// 这个验证器在**每个应用**启动时都跑（`CoreServicesModule` 无条件加载），
/// 所以它误报一次的代价是「一个根本不用 AI 的应用起不来」。用例因此既钉死它该拦的，
/// 也钉死它<b>刻意不拦</b>的。
/// </remarks>
public class AiProviderRegistryOptionsTests
{
    // ------------------------------------------------------------------
    // ResolveEnabled
    // ------------------------------------------------------------------

    [Fact]
    public void ResolveEnabled_ReturnsDefaultProvider()
    {
        var options = Registry(("main", true), ("other", true));

        Assert.Same(options.Providers["main"], options.ResolveEnabled());
    }

    [Fact]
    public void ResolveEnabled_NamedProvider_IgnoresDefault()
    {
        var options = Registry(("main", true), ("other", true));

        Assert.Same(options.Providers["other"], options.ResolveEnabled("other"));
    }

    [Fact]
    public void ResolveEnabled_DisabledProvider_ReturnsNull()
    {
        Assert.Null(Registry(("main", false)).ResolveEnabled());
    }

    [Fact]
    public void ResolveEnabled_UnknownProvider_ReturnsNull()
    {
        Assert.Null(Registry(("main", true)).ResolveEnabled("nope"));
    }

    [Fact]
    public void ResolveEnabled_NoProviders_ReturnsNull()
    {
        Assert.Null(new AiProviderRegistryOptions().ResolveEnabled());
    }

    [Fact]
    public void ResolveEnabled_BlankDefaultProvider_ReturnsNull()
    {
        var options = Registry(("main", true));
        options.DefaultProvider = "  ";

        Assert.Null(options.ResolveEnabled());
    }

    // ------------------------------------------------------------------
    // 该拦的
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_DefaultProviderNotInDictionary_Fails()
    {
        var options = Registry(("main", true));
        options.DefaultProvider = "missing";

        Assert.Contains(Validate(options), e => e.Contains("is not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DefaultProviderNotInDictionary_FailsEvenWithAnUnrelatedDisabledEntry()
    {
        // 真在用 AI 的应用不因为字典里混进了一条别处来的条目就免检：只要有**一个**已启用的提供商，
        // DefaultProvider 写错就仍然启动即失败。这是上面那条放行的边界，两条必须同时成立。
        var options = Registry(("main", true));
        options.Providers["DeepSeek"] = new AiProviderOptions { Name = "DeepSeek", ApiKey = "sk-someone-elses-app" };
        options.DefaultProvider = "typo";

        Assert.Contains(Validate(options), e => e.Contains("is not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DefaultProviderNotInDictionary_MessageNamesTheKeysThatAreActuallyThere()
    {
        // 这条错误最贵的形态不是「名字打错了」，而是「应用根本没写过 AI 配置，键是别的配置源来的」。
        // 消息里既要有实际存在的键名，也要指出键可能来自环境变量 —— 否则排查要从「我明明没配 AI」开始。
        var options = Registry(("main", true));
        options.DefaultProvider = "typo";

        var error = Assert.Single(Validate(options));
        Assert.Contains("main", error, StringComparison.Ordinal);
        Assert.Contains("AI__Providers__", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_BlankDefaultProviderWithProvidersPresent_Fails()
    {
        var options = Registry(("main", true));
        options.DefaultProvider = "";

        Assert.Contains(Validate(options), e => e.Contains("DefaultProvider", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/v1")]
    [InlineData("ftp://example.com/v1")]
    public void Validate_BadBaseUrl_Fails(string baseUrl)
    {
        var options = Registry(("main", true));
        options.Providers["main"].BaseUrl = baseUrl;

        Assert.Contains(Validate(options), e => e.Contains("BaseUrl", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(601)]
    public void Validate_TimeoutOutOfRange_Fails(int timeout)
    {
        var options = Registry(("main", true));
        options.Providers["main"].TimeoutSeconds = timeout;

        Assert.Contains(Validate(options), e => e.Contains("TimeoutSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void Validate_TemperatureOutOfRange_Fails(double temperature)
    {
        var options = Registry(("main", true));
        options.Providers["main"].Temperature = temperature;

        Assert.Contains(Validate(options), e => e.Contains("Temperature", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ModelAliasWithEmptyTarget_Fails()
    {
        var options = Registry(("main", true));
        options.Providers["main"].Models = new Dictionary<string, string> { ["fast"] = "  " };

        Assert.Contains(Validate(options), e => e.Contains("Models alias", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------
    // 刻意不拦的 —— 每一条误报都会让一个不用 AI 的应用起不来
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_NoProvidersAtAll_Passes()
    {
        // 绝大多数应用属于这一类：配置里根本没有 AI 节。
        Assert.Empty(Validate(new AiProviderRegistryOptions()));
    }

    [Fact]
    public void Validate_ProviderEntryWithoutEnabled_DoesNotRequireDefaultProvider()
    {
        // ★消费方实际撞上的那个失败：应用自己一个字都没写过 AI 配置，字典却非空 ——
        // 一条为同机器上另一个应用设的用户级环境变量 AI__Providers__DeepSeek__ApiKey
        // 会绑进**每一个** Tnzi 应用的这个字典。DefaultProvider 停在类型默认值 "OpenAI"，
        // 于是「字典非空 ⇒ DefaultProvider 必须是其中一个键」把整个宿主挡在启动之外，
        // 而应用改不掉那个变量（全机器共享，且每开一个 shell 都要重新清）。
        var options = new AiProviderRegistryOptions();
        options.Providers["DeepSeek"] = new AiProviderOptions { Name = "DeepSeek", ApiKey = "sk-someone-elses-app" };

        Assert.Empty(Validate(options));

        // 同时这个应用对外必须诚实：解析不到提供商，IAiUtility 会照实报不可用。
        Assert.Null(options.ResolveEnabled());
    }

    [Fact]
    public void Validate_AllProvidersDisabled_DoesNotRequireDefaultProvider()
    {
        // 一个提供商都没启用时 DefaultProvider 指向谁都解析不出东西，校验它没有任何运行时后果。
        // 重新启用的那一次启动会照常 fail-fast（见 Validate_DefaultProviderNotInDictionary_Fails）。
        var options = Registry(("main", false), ("spare", false));
        options.DefaultProvider = "missing";

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void Validate_DisabledDefaultProvider_Passes()
    {
        // ★把提供商 Enabled 置 false 是「临时关掉 AI」的合法做法。
        // Tnzi.AI 的 AIOptionsValidator 对此 fail-fast，核心刻意不 —— 关掉就是关掉，不是配置错误。
        var options = Registry(("main", false));

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void Validate_DisabledProviderWithInvalidFields_Passes()
    {
        // 已禁用的条目不参与校验：留着一份写坏的备用配置不该阻塞启动。
        var options = Registry(("main", true), ("spare", false));
        options.Providers["spare"].BaseUrl = "not-a-url";
        options.Providers["spare"].TimeoutSeconds = -5;

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void Validate_EnabledProviderWithoutApiKey_Passes()
    {
        // ApiKey 可以来自环境变量 AI__{NAME}__APIKEY，启动期看不到；
        // 在这里拦会把「用环境变量注入密钥」这种标准做法判成配置错误。
        var options = Registry(("main", true));
        options.Providers["main"].ApiKey = null;

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void Validate_EnabledProviderWithoutBaseUrl_Passes()
    {
        // 留空 = 用官方 OpenAI 端点。
        var options = Registry(("main", true));
        options.Providers["main"].BaseUrl = null;

        Assert.Empty(Validate(options));
    }

    // ------------------------------------------------------------------
    // 绑定 + 校验（走真实的 AddTnziOptions 注册，不是直接 new 一个验证器）
    // ------------------------------------------------------------------

    [Fact]
    public void StartupValidation_SurvivesAProviderKeyThatOnlyTheEnvironmentSupplied()
    {
        // 这里的键就是环境变量 AI__Providers__DeepSeek__ApiKey 经 .NET 的 "__" → ":" 归一之后的形态。
        // ★刻意不真的去 SetEnvironmentVariable：那是进程级全局状态，并行跑的其它测试会看见它。
        // 归一由配置系统负责，本用例要钉的是它之后的那一段：绑定出什么、校验器怎么判。
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Providers:DeepSeek:ApiKey"] = "sk-someone-elses-app"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddTnziOptions<AiProviderRegistryOptions, AiProviderRegistryOptionsValidator>(configuration);

        using var provider = services.BuildServiceProvider();

        // 取 Value 就会跑验证器 —— 宿主启动时 ValidateOnStart 做的正是这一下。
        var options = provider.GetRequiredService<IOptions<AiProviderRegistryOptions>>().Value;

        // 那条环境变量确实把字典撑成了非空，而条目的 Enabled 停在类型默认值 false ——
        // 「不使用 AI」与「这个字典是空的」从来就不是一回事。
        Assert.Equal("DeepSeek", Assert.Single(options.Providers).Key);
        Assert.False(options.Providers["DeepSeek"].Enabled);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static AiProviderRegistryOptions Registry(params (string Name, bool Enabled)[] providers)
    {
        var options = new AiProviderRegistryOptions { DefaultProvider = providers[0].Name };

        foreach (var (name, enabled) in providers)
        {
            options.Providers[name] = new AiProviderOptions
            {
                Name = name,
                Enabled = enabled,
                ApiKey = "sk-test",
                BaseUrl = "https://api.example.com/v1",
                DefaultModel = "test-model"
            };
        }

        return options;
    }

    private static IReadOnlyList<string> Validate(AiProviderRegistryOptions options)
    {
        var result = new AiProviderRegistryOptionsValidator().Validate(name: null, options);
        return result.Failed ? result.Failures?.ToList() ?? [] : [];
    }
}
