using System.Text;
using Microsoft.Extensions.Configuration;
using Tnzi.Options;

namespace Tnzi.Finance.Ai.Tests;

/// <summary>
/// <c>Finance:Ai:VisionContentTypes</c> 经真实的配置绑定（JSON → <c>AddTnziOptions</c>）后是什么。
/// </summary>
/// <remarks>
/// 只在代码里给属性赋值的测试绕过了绑定器，而绑定器对数组是<b>追加</b>语义、JSON 空数组又不产生任何键：
/// 属性初值若预置了四种格式，配置既收窄不了它（<c>["image/png"]</c> 绑成五项），也清不空它
/// （<c>[]</c> 什么都不改），校验器还看不出任何问题。这几条锁的是操作员写在 appsettings 里的样子。
/// </remarks>
public class FinanceAiOptionsBindingTests
{
    private static FinanceAiOptions Bind(string financeAiJson)
    {
        var json = $"{{ \"Finance\": {{ \"Ai\": {financeAiJson} }} }}";
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();

        var services = new ServiceCollection();
        services.AddTnziOptions<FinanceAiOptions, FinanceAiOptionsValidator>(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<FinanceAiOptions>>().Value;
    }

    [Fact]
    public void Unconfigured_resolves_to_the_built_in_list()
    {
        var options = Bind("{}");

        options.ResolveVisionContentTypes().ShouldBe(FinanceAiOptions.DefaultVisionContentTypes);
    }

    /// <summary>JSON 空数组与「没配」无从区分，所以它的意思是缺省清单，不是「不拦」。</summary>
    [Fact]
    public void Empty_json_array_resolves_to_the_built_in_list()
    {
        var options = Bind("{ \"VisionContentTypes\": [] }");

        options.ResolveVisionContentTypes().ShouldBe(FinanceAiOptions.DefaultVisionContentTypes);
    }

    [Fact]
    public void Configured_list_replaces_the_defaults_instead_of_appending_to_them()
    {
        var options = Bind("{ \"VisionContentTypes\": [\"image/png\"] }");

        options.ResolveVisionContentTypes().ShouldBe(["image/png"]);
    }

    [Fact]
    public void Wildcard_disables_the_gate()
    {
        var options = Bind("{ \"VisionContentTypes\": [\"*\"] }");

        options.ResolveVisionContentTypes().ShouldBeNull();
    }

    [Fact]
    public void Wildcard_mixed_with_content_types_fails_validation()
    {
        Should.Throw<OptionsValidationException>(() => Bind("{ \"VisionContentTypes\": [\"*\", \"image/png\"] }"));
    }
}
