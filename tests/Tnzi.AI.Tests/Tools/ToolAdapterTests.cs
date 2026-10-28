using Tnzi.AI.Tools.Attributes;

namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// <see cref="ToolAdapter"/> 必须把 <c>ToolDefinition</c> 上的安全元数据带到 <c>AIFunction.AdditionalProperties</c>，
/// 否则权限评估器看不到 <c>[AIFunction(IsDestructive = true)]</c>。
/// </summary>
public class ToolAdapterTests
{
    [Fact]
    public void ConvertToAITools_CopiesIsDestructiveAndReadOnlyIntoAdditionalProperties()
    {
        var scanner = new ToolScanner(NullLogger<ToolScanner>.Instance);
        var definitions = scanner.ScanAssembly(typeof(MetadataTools).Assembly)
            .Where(t => t.ProviderType == typeof(MetadataTools))
            .ToList();
        var services = new ServiceCollection().AddSingleton<MetadataTools>().BuildServiceProvider();

        var functions = ToolAdapter.ConvertToAITools(definitions, services).Cast<AIFunction>().ToList();

        var destructive = functions.Single(f => f.Name == "wipe");
        destructive.AdditionalProperties[ToolMetadataKeys.Destructive].ShouldBe(true);
        destructive.AdditionalProperties[ToolMetadataKeys.ReadOnly].ShouldBe(false);

        var safe = functions.Single(f => f.Name == "peek");
        safe.AdditionalProperties[ToolMetadataKeys.Destructive].ShouldBe(false);
        safe.AdditionalProperties[ToolMetadataKeys.ReadOnly].ShouldBe(true);
    }

    [AIToolGroup("adapter-metadata", "Adapter Metadata Tools", "Tools for testing adapter metadata")]
    private class MetadataTools : IAIToolProvider
    {
        [AIFunction("wipe", "Wipes things", IsDestructive = true)]
        public Task<string> WipeAsync() => Task.FromResult("gone");

        [AIFunction("peek", "Reads things", IsReadOnly = true)]
        public Task<string> PeekAsync() => Task.FromResult("seen");
    }

    // ---- [AIParameter] 的 name / description / required 必须进入交给模型的 schema，并按 name 绑定 ----

    private static List<AIFunction> ConvertParameterTools()
    {
        var scanner = new ToolScanner(NullLogger<ToolScanner>.Instance);
        var definitions = scanner.ScanAssembly(typeof(ParameterTools).Assembly)
            .Where(t => t.ProviderType == typeof(ParameterTools))
            .ToList();
        var services = new ServiceCollection().AddSingleton<ParameterTools>().BuildServiceProvider();
        return ToolAdapter.ConvertToAITools(definitions, services).Cast<AIFunction>().ToList();
    }

    private static AIFunctionArguments ArgumentsFromJson(string json)
        => new(JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!);

    [Fact]
    public void AIParameter_NameDescriptionAndRequired_ShapeTheSchema()
    {
        var forecast = ConvertParameterTools().Single(f => f.Name == "forecast");
        var schema = forecast.JsonSchema;
        var properties = schema.GetProperty("properties");

        properties.TryGetProperty("city", out var city).ShouldBeTrue();
        properties.TryGetProperty("cityName", out _).ShouldBeFalse();
        city.GetProperty("description").GetString().ShouldBe("City name");

        properties.GetProperty("unit").GetProperty("description").GetString().ShouldBe("celsius or fahrenheit");
        properties.GetProperty("days").GetProperty("description").GetString().ShouldBe("Forecast days");

        // 没有 [AIParameter] 的参数保持原样
        properties.TryGetProperty("note", out _).ShouldBeTrue();

        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        required.ShouldBe(new[] { "city", "days", "note" }, ignoreOrder: true);
    }

    [Fact]
    public async Task AIParameter_ArgumentsAreBoundByTheDeclaredName_AndOptionalOnesMayBeOmitted()
    {
        var forecast = ConvertParameterTools().Single(f => f.Name == "forecast");

        var result = await forecast.InvokeAsync(ArgumentsFromJson("""{"city":"Paris","days":3,"note":"n"}"""));

        result!.ToString().ShouldBe("Paris||3|n");
    }

    [Fact]
    public async Task AIParameter_TheCSharpParameterNamesAreStillAccepted()
    {
        var forecast = ConvertParameterTools().Single(f => f.Name == "forecast");

        var result = await forecast.InvokeAsync(ArgumentsFromJson("""{"cityName":"Oslo","unit":"celsius","days":2,"note":"x"}"""));

        result!.ToString().ShouldBe("Oslo|celsius|2|x");
    }

    [Fact]
    public void AIParameter_NameThatCollidesWithAnotherParameter_DropsTheToolInsteadOfShadowingIt()
    {
        var functions = ConvertParameterTools();

        functions.ShouldNotContain(f => f.Name == "colliding");
        functions.ShouldContain(f => f.Name == "forecast");
    }

    [AIToolGroup("adapter-parameters", "Adapter Parameter Tools", "Tools for testing parameter metadata")]
    private class ParameterTools : IAIToolProvider
    {
        [AIFunction("forecast", "Gets a forecast")]
        public Task<string> ForecastAsync(
            [AIParameter("city", "City name")] string cityName,
            [AIParameter("unit", "celsius or fahrenheit", false)] string? unit,
            string note,
            [AIParameter("days", "Forecast days", true)] int days = 1)
            => Task.FromResult($"{cityName}|{unit}|{days}|{note}");

        [AIFunction("colliding", "Two parameters exposed under one name")]
        public Task<string> CollidingAsync(
            [AIParameter("query", "Search text")] string text,
            string query)
            => Task.FromResult(text + query);
    }
}
