namespace Tnzi.AI.Tests;

/// <summary>
/// SimplePromptTemplateEngine 单元测试
/// </summary>
public class PromptTemplateEngineTests
{
    [Fact]
    public void Render_NoTemplate_ReturnsOriginal()
    {
        var engine = new SimplePromptTemplateEngine();
        var result = engine.Render("Hello, world!");
        result.ShouldBe("Hello, world!");
    }

    [Fact]
    public void Render_NullTemplate_ReturnsEmpty()
    {
        var engine = new SimplePromptTemplateEngine();
        var result = engine.Render(null!);
        result.ShouldBeNull();
    }

    [Fact]
    public void Render_EmptyTemplate_ReturnsEmpty()
    {
        var engine = new SimplePromptTemplateEngine();
        var result = engine.Render(string.Empty);
        result.ShouldBe(string.Empty);
    }

    [Fact]
    public void Render_CustomVariable_Replaced()
    {
        var engine = new SimplePromptTemplateEngine();
        var vars = new Dictionary<string, string> { ["role"] = "assistant" };
        var result = engine.Render("You are a {{role}}.", vars);
        result.ShouldBe("You are a assistant.");
    }

    [Fact]
    public void Render_MultipleVariables_AllReplaced()
    {
        var engine = new SimplePromptTemplateEngine();
        var vars = new Dictionary<string, string>
        {
            ["role"] = "translator",
            ["lang"] = "Chinese"
        };
        var result = engine.Render("You are a {{role}}. Translate to {{lang}}.", vars);
        result.ShouldBe("You are a translator. Translate to Chinese.");
    }

    [Fact]
    public void Render_UnknownVariable_PreservesPlaceholder()
    {
        var engine = new SimplePromptTemplateEngine();
        var result = engine.Render("Hello {{unknown}}!");
        result.ShouldBe("Hello {{unknown}}!");
    }

    [Fact]
    public void Render_BuiltInDate_ReplacedWithCurrentDate()
    {
        var engine = new SimplePromptTemplateEngine();
        var result = engine.Render("Today is {{date}}.");
        result.ShouldContain(DateTime.UtcNow.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void Render_BuiltInDatetime_ReplacedWithCurrentDatetime()
    {
        var engine = new SimplePromptTemplateEngine();
        var result = engine.Render("Now is {{datetime}}.");
        result.ShouldContain(DateTime.UtcNow.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void Render_CustomOverridesBuiltIn()
    {
        var engine = new SimplePromptTemplateEngine();
        var vars = new Dictionary<string, string> { ["date"] = "2025-01-01" };
        var result = engine.Render("Date: {{date}}", vars);
        result.ShouldBe("Date: 2025-01-01");
    }

    [Fact]
    public void Render_CaseInsensitiveVariables()
    {
        var engine = new SimplePromptTemplateEngine();
        var vars = new Dictionary<string, string> { ["Role"] = "helper" };
        // 内置变量匹配使用 OrdinalIgnoreCase
        var result = engine.Render("Date: {{DATE}}", null);
        result.ShouldContain(DateTime.UtcNow.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void Render_VariableWithSpaces_Trimmed()
    {
        var engine = new SimplePromptTemplateEngine();
        var vars = new Dictionary<string, string> { ["name"] = "Alice" };
        var result = engine.Render("Hello {{ name }}!", vars);
        result.ShouldBe("Hello Alice!");
    }

    [Fact]
    public void Render_WithCurrentUser_PopulatesUserVariables()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.UserName).Returns("TestUser");
        currentUser.Setup(u => u.Id).Returns(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var engine = new SimplePromptTemplateEngine(currentUser.Object);
        var result = engine.Render("User: {{user.name}}, ID: {{user.id}}");
        result.ShouldBe("User: TestUser, ID: 11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void Render_AgentNameVariable_FromCustomVars()
    {
        var engine = new SimplePromptTemplateEngine();
        var vars = new Dictionary<string, string> { ["agent.name"] = "MyAgent" };
        var result = engine.Render("I am {{agent.name}}.", vars);
        result.ShouldBe("I am MyAgent.");
    }

    [Fact]
    public void Render_MixedKnownAndUnknown_PartialReplacement()
    {
        var engine = new SimplePromptTemplateEngine();
        var vars = new Dictionary<string, string> { ["name"] = "Bob" };
        var result = engine.Render("Hello {{name}}, today is {{date}}, weather is {{weather}}.", vars);
        result.ShouldContain("Hello Bob");
        result.ShouldContain("{{weather}}");
    }
}
