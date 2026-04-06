namespace Tnzi.AI.Tests.Workspace;

public class AgentMarkdownParserTests
{
    [Fact]
    public void Parse_ValidAgentMd_ExtractsAllFields()
    {
        // Arrange
        var content = """
            ---
            name: Research Assistant
            description: A helpful research agent
            provider: OpenAI
            model: gpt-4o
            temperature: 0.7
            maxTokens: 4096
            toolGroups: [web-search, memory]
            executionMode: Single
            domains: [research, analysis]
            roles: [researcher]
            qualityTier: 3
            ---
            You are a research assistant.

            ## Guidelines
            - Always cite sources
            - Be thorough
            """;

        // Act
        var result = AgentMarkdownParser.Parse(content, "research-assistant");

        // Assert
        result.ShouldNotBeNull();
        result.AgentId.ShouldBe("research-assistant");
        result.Name.ShouldBe("Research Assistant");
        result.Description.ShouldBe("A helpful research agent");
        result.Provider.ShouldBe("OpenAI");
        result.Model.ShouldBe("gpt-4o");
        result.Temperature.ShouldBe(0.7f);
        result.MaxTokens.ShouldBe(4096);
        result.ToolGroups.ShouldNotBeNull();
        result.ToolGroups.ShouldBe(["web-search", "memory"]);
        result.ExecutionMode.ShouldBe("Single");
        result.Domains.ShouldNotBeNull();
        result.Domains.ShouldBe(["research", "analysis"]);
        result.Roles.ShouldNotBeNull();
        result.Roles.ShouldBe(["researcher"]);
        result.QualityTier.ShouldBe(3);
        result.Instructions.ShouldNotBeNull();
        result.Instructions.ShouldContain("research assistant");
        result.Instructions.ShouldContain("## Guidelines");
    }

    [Fact]
    public void Parse_MinimalFrontmatter_UsesDefaults()
    {
        // Arrange
        var content = """
            ---
            name: Simple Agent
            ---
            """;

        // Act
        var result = AgentMarkdownParser.Parse(content, "simple");

        // Assert
        result.ShouldNotBeNull();
        result.AgentId.ShouldBe("simple");
        result.Name.ShouldBe("Simple Agent");
        result.Description.ShouldBeNull();
        result.Provider.ShouldBeNull();
        result.Model.ShouldBeNull();
        result.Temperature.ShouldBeNull();
        result.MaxTokens.ShouldBeNull();
        result.ToolGroups.ShouldBeNull();
        result.ExecutionMode.ShouldBeNull();
        result.Domains.ShouldBeNull();
        result.Roles.ShouldBeNull();
        result.QualityTier.ShouldBeNull();
    }

    [Fact]
    public void Parse_NoFrontmatter_ReturnsNull()
    {
        // Arrange
        var content = """
            # Just a markdown file
            No frontmatter here.
            """;

        // Act
        var result = AgentMarkdownParser.Parse(content, "test");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsNull()
    {
        AgentMarkdownParser.Parse("", "test").ShouldBeNull();
        AgentMarkdownParser.Parse("   ", "test").ShouldBeNull();
        AgentMarkdownParser.Parse(null!, "test").ShouldBeNull();
    }

    [Fact]
    public void ParsePersona_ExtractsFields()
    {
        // Arrange
        var content = """
            ---
            name: Friendly Helper
            tone: warm
            language: en
            ---
            You are a friendly and warm assistant who loves to help.
            Always greet the user first.
            """;

        // Act
        var result = AgentMarkdownParser.ParsePersona(content);

        // Assert
        result.ShouldNotBeNull();
        result.Name.ShouldBe("Friendly Helper");
        result.Tone.ShouldBe("warm");
        result.Language.ShouldBe("en");
        result.Content.ShouldContain("friendly and warm assistant");
        result.Content.ShouldContain("Always greet the user first.");
    }

    [Fact]
    public void Parse_ListFormats_BothWork()
    {
        // Test inline list format [a, b]
        var inlineContent = """
            ---
            name: Inline Agent
            toolGroups: [search, memory, coding]
            ---
            """;

        var inlineResult = AgentMarkdownParser.Parse(inlineContent, "inline");
        inlineResult.ShouldNotBeNull();
        inlineResult.ToolGroups.ShouldNotBeNull();
        inlineResult.ToolGroups.ShouldBe(["search", "memory", "coding"]);

        // Test multi-line list format
        var multilineContent = "---\nname: Multiline Agent\ntoolGroups:\n- search\n- memory\n- coding\n---\n";

        var multilineResult = AgentMarkdownParser.Parse(multilineContent, "multiline");
        multilineResult.ShouldNotBeNull();
        multilineResult.ToolGroups.ShouldNotBeNull();
        multilineResult.ToolGroups.ShouldBe(["search", "memory", "coding"]);
    }

    [Fact]
    public void Parse_MissingNameField_ReturnsNull()
    {
        // Arrange - frontmatter without name
        var content = """
            ---
            description: No name agent
            provider: OpenAI
            ---
            Some instructions
            """;

        // Act
        var result = AgentMarkdownParser.Parse(content, "test");

        // Assert
        result.ShouldBeNull();
    }
}
