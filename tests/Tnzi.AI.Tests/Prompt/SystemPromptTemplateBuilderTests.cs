namespace Tnzi.AI.Tests.Prompt;

public class SystemPromptTemplateBuilderTests
{
    [Fact]
    public void WrapInstructions_NonEmptyContent_WrapsInInstructionsTag()
    {
        var result = SystemPromptTemplateBuilder.WrapInstructions("Follow rules.");

        // 与 13-tag 装配管线删除前的输出逐字节一致
        result.ShouldBe("<instructions>\nFollow rules.\n</instructions>");
    }

    [Fact]
    public void WrapInstructions_MultilineContent_PreservesContentVerbatim()
    {
        const string content = "Line one.\nLine two.\n\nLine four.";
        var result = SystemPromptTemplateBuilder.WrapInstructions(content);

        result.ShouldBe($"<instructions>\n{content}\n</instructions>");
    }

    [Fact]
    public void WrapInstructions_Null_ReturnsNull()
    {
        SystemPromptTemplateBuilder.WrapInstructions(null).ShouldBeNull();
    }

    [Fact]
    public void WrapInstructions_Empty_ReturnsNull()
    {
        SystemPromptTemplateBuilder.WrapInstructions(string.Empty).ShouldBeNull();
    }

    [Fact]
    public void WrapInstructions_Whitespace_ReturnsNull()
    {
        SystemPromptTemplateBuilder.WrapInstructions("   ").ShouldBeNull();
    }
}
