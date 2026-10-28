namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// <c>show_widget</c>: the engine's tool (<see cref="WidgetTools"/>) and the definition an application's
/// MCP server declares for CLI agents (<see cref="WidgetToolDefinition"/>) must describe the same tool.
/// The model of either path then writes the same call, and one front-end renderer draws both.
/// </summary>
public class WidgetToolsTests
{
    private static AIFunction EngineFunction()
    {
        var scanner = new ToolScanner(NullLogger<ToolScanner>.Instance);
        var definitions = scanner.ScanAssembly(typeof(WidgetTools).Assembly)
            .Where(t => t.ProviderType == typeof(WidgetTools))
            .ToList();
        var services = new ServiceCollection().AddSingleton<WidgetTools>().BuildServiceProvider();
        return ToolAdapter.ConvertToAITools(definitions, services).Cast<AIFunction>().Single();
    }

    [Fact]
    public void EngineTool_MatchesTheSharedDefinition()
    {
        var function = EngineFunction();

        function.Name.ShouldBe(WidgetToolDefinition.Name);
        function.Description.ShouldBe(WidgetToolDefinition.Description);

        using var declared = JsonDocument.Parse(WidgetToolDefinition.InputSchemaJson);
        var declaredProperties = declared.RootElement.GetProperty("properties");
        var engineProperties = function.JsonSchema.GetProperty("properties");

        var declaredNames = declaredProperties.EnumerateObject().Select(p => p.Name).ToList();
        engineProperties.EnumerateObject().Select(p => p.Name).ShouldBe(declaredNames, ignoreOrder: true);
        foreach (var name in declaredNames)
        {
            engineProperties.GetProperty(name).GetProperty("description").GetString()
                .ShouldBe(declaredProperties.GetProperty(name).GetProperty("description").GetString());
        }

        function.JsonSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(declared.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()), ignoreOrder: true);
    }

    [Fact]
    public void DeclaredSchema_LimitsAreTheDefinitionsLimits()
    {
        using var declared = JsonDocument.Parse(WidgetToolDefinition.InputSchemaJson);
        var properties = declared.RootElement.GetProperty("properties");

        properties.GetProperty(WidgetToolDefinition.TitleParameter).GetProperty("maxLength").GetInt32()
            .ShouldBe(WidgetToolDefinition.MaxTitleLength);
        properties.GetProperty(WidgetToolDefinition.HtmlParameter).GetProperty("maxLength").GetInt32()
            .ShouldBe(WidgetToolDefinition.MaxHtmlLength);
        properties.GetProperty(WidgetToolDefinition.TitleParameter).GetProperty("description").GetString()
            .ShouldBe(WidgetToolDefinition.TitleDescription);
        properties.GetProperty(WidgetToolDefinition.HtmlParameter).GetProperty("description").GetString()
            .ShouldBe(WidgetToolDefinition.HtmlDescription);
    }

    [Fact]
    public async Task EngineTool_ValidCall_Acknowledges()
    {
        var result = await EngineFunction().InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["title"] = "Usage",
            ["html"] = "<!DOCTYPE html><html><body><input type=range></body></html>"
        }));

        result?.ToString().ShouldBe(WidgetToolDefinition.SuccessResult);
    }

    [Fact]
    public void ShowWidget_InvalidCall_ThrowsSoTheCallIsReportedAsFailed()
    {
        var tools = new WidgetTools();

        Should.Throw<ArgumentException>(() => tools.ShowWidget("Usage", " "))
            .Message.ShouldContain(WidgetToolDefinition.HtmlParameter);
        Should.Throw<ArgumentException>(() => tools.ShowWidget("", "<p>x</p>"))
            .Message.ShouldContain(WidgetToolDefinition.TitleParameter);
    }

    [Theory]
    [InlineData(null, "<p>x</p>", "title")]
    [InlineData("  ", "<p>x</p>", "title")]
    [InlineData("Usage", null, "html")]
    [InlineData("Usage", "", "html")]
    public void Validate_MissingArguments_SaysWhichOne(string? title, string? html, string expected)
    {
        WidgetToolDefinition.Validate(title, html).ShouldNotBeNull().ShouldContain($"'{expected}'");
    }

    [Fact]
    public void Validate_EnforcesTheLimits()
    {
        WidgetToolDefinition.Validate(new string('t', WidgetToolDefinition.MaxTitleLength), "<p>x</p>").ShouldBeNull();
        WidgetToolDefinition.Validate(new string('t', WidgetToolDefinition.MaxTitleLength + 1), "<p>x</p>").ShouldNotBeNull();

        WidgetToolDefinition.Validate("Usage", new string('h', WidgetToolDefinition.MaxHtmlLength)).ShouldBeNull();
        WidgetToolDefinition.Validate("Usage", new string('h', WidgetToolDefinition.MaxHtmlLength + 1)).ShouldNotBeNull();
    }

    [Fact]
    public void Validate_JsonArguments_ReadsTheSameFields()
    {
        using var valid = JsonDocument.Parse("""{"title":"Usage","html":"<p>x</p>"}""");
        WidgetToolDefinition.Validate(valid.RootElement).ShouldBeNull();

        using var wrongType = JsonDocument.Parse("""{"title":"Usage","html":42}""");
        WidgetToolDefinition.Validate(wrongType.RootElement).ShouldNotBeNull().ShouldContain("'html'");

        using var notAnObject = JsonDocument.Parse("""["Usage","<p>x</p>"]""");
        WidgetToolDefinition.Validate(notAnObject.RootElement).ShouldNotBeNull();
    }
}
