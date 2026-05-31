namespace Tnzi.AI.Tests.Prompt;

public class SystemPromptTemplateBuilderTests
{
    [Fact]
    public async Task BuildAsync_EmptyBuilder_ReturnsEmptyString()
    {
        var builder = new SystemPromptTemplateBuilder();
        var result = await builder.BuildAsync();
        result.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task BuildAsync_SingleSection_WrapsInXmlTag()
    {
        var builder = new SystemPromptTemplateBuilder();
        builder.AddSection("soul", "You are a helpful assistant.", order: 0);
        var result = await builder.BuildAsync();
        result.ShouldContain("<soul>");
        result.ShouldContain("You are a helpful assistant.");
        result.ShouldContain("</soul>");
    }

    [Fact]
    public async Task BuildAsync_MultipleSections_OrderedByOrderValue()
    {
        var builder = new SystemPromptTemplateBuilder();
        builder.AddSection("current_date", "2026-03-28", order: 120);
        builder.AddSection("soul", "You are helpful.", order: 0);
        builder.AddSection("instructions", "Follow rules.", order: 30);

        var result = await builder.BuildAsync();
        var soulIdx = result.IndexOf("<soul>");
        var instrIdx = result.IndexOf("<instructions>");
        var dateIdx = result.IndexOf("<current_date>");

        soulIdx.ShouldBeLessThan(instrIdx);
        instrIdx.ShouldBeLessThan(dateIdx);
    }

    [Fact]
    public async Task BuildAsync_NullOrEmptyContent_SkipsSection()
    {
        var builder = new SystemPromptTemplateBuilder();
        builder.AddSection("soul", "Content here.", order: 0);
        builder.AddSection("memory", null, order: 20);
        builder.AddSection("instructions", "", order: 30);

        var result = await builder.BuildAsync();
        result.ShouldContain("<soul>");
        result.ShouldNotContain("<memory>");
        result.ShouldNotContain("<instructions>");
    }

    [Fact]
    public async Task BuildAsync_DuplicateTag_LastWins()
    {
        var builder = new SystemPromptTemplateBuilder();
        builder.AddSection("soul", "First version.", order: 0);
        builder.AddSection("soul", "Second version.", order: 0);

        var result = await builder.BuildAsync();
        result.ShouldNotContain("First version.");
        result.ShouldContain("Second version.");
    }

    [Fact]
    public async Task BuildAsync_All13Sections_ContainsAllTags()
    {
        var builder = new SystemPromptTemplateBuilder();
        builder.AddSection("soul", "persona", order: 0);
        builder.AddSection("user_profile", "profile", order: 10);
        builder.AddSection("memory", "memories", order: 20);
        builder.AddSection("instructions", "rules", order: 30);
        builder.AddSection("skill_system", "skills", order: 40);
        builder.AddSection("available-deferred-tools", "tools", order: 50);
        builder.AddSection("clarification_system", "clarify", order: 60);
        builder.AddSection("sub_agent_orchestration", "delegation", order: 70);
        builder.AddSection("working_directory", "paths", order: 80);
        builder.AddSection("response_style", "format", order: 90);
        builder.AddSection("citations", "cite", order: 100);
        builder.AddSection("critical_reminders", "reminders", order: 110);
        builder.AddSection("current_date", "2026-03-28", order: 120);

        var result = await builder.BuildAsync();
        var tags = new[] { "soul", "user_profile", "memory", "instructions", "skill_system",
            "available-deferred-tools", "clarification_system", "sub_agent_orchestration",
            "working_directory", "response_style", "citations", "critical_reminders", "current_date" };

        foreach (var tag in tags)
        {
            result.ShouldContain($"<{tag}>");
            result.ShouldContain($"</{tag}>");
        }
    }

    [Fact]
    public async Task AddSectionProvider_IntegratesProviderOutput()
    {
        var builder = new SystemPromptTemplateBuilder();
        builder.AddSection("soul", "Base persona.", order: 0);
        builder.AddSectionProvider(new TestSectionProvider("memory", "Recalled: user likes C#", 20));

        var result = await builder.BuildAsync();
        result.ShouldContain("<soul>");
        result.ShouldContain("<memory>");
        result.ShouldContain("Recalled: user likes C#");
    }

    [Fact]
    public async Task RemoveSection_RemovesTag()
    {
        var builder = new SystemPromptTemplateBuilder();
        builder.AddSection("soul", "persona", order: 0);
        builder.AddSection("memory", "memories", order: 20);
        builder.RemoveSection("memory");

        var result = await builder.BuildAsync();
        result.ShouldContain("<soul>");
        result.ShouldNotContain("<memory>");
    }

    private sealed class TestSectionProvider(string tag, string content, int order) : ISystemPromptSectionProvider
    {
        public Task<SystemPromptSection?> GetSectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<SystemPromptSection?>(new(tag, content, order));
    }
}
