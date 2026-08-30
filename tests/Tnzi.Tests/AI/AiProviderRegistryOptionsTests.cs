using Tnzi.AI.Options;

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
