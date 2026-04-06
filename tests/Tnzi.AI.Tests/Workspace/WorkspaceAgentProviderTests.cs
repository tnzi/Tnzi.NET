namespace Tnzi.AI.Tests.Workspace;

public class WorkspaceAgentProviderTests : IDisposable
{
    private readonly string _testDir;
    private readonly WorkspaceAgentProvider _provider;

    public WorkspaceAgentProviderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "tnzi-workspace-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
        _provider = new WorkspaceAgentProvider(NullLogger<WorkspaceAgentProvider>.Instance);
    }

    [Fact]
    public async Task DiscoverAsync_FindsAgentMdFiles()
    {
        // Arrange
        var agentDir = Path.Combine(_testDir, "my-agent");
        Directory.CreateDirectory(agentDir);
        await File.WriteAllTextAsync(Path.Combine(agentDir, "AGENT.md"), """
            ---
            name: My Agent
            provider: OpenAI
            model: gpt-4o
            ---
            You are a helpful assistant.
            """);

        var agentDir2 = Path.Combine(_testDir, "other-agent");
        Directory.CreateDirectory(agentDir2);
        await File.WriteAllTextAsync(Path.Combine(agentDir2, "AGENT.md"), """
            ---
            name: Other Agent
            ---
            Another agent.
            """);

        // Act
        var results = await _provider.DiscoverAsync(_testDir);

        // Assert
        results.Count.ShouldBe(2);
        results.Select(r => r.Name).ShouldContain("My Agent");
        results.Select(r => r.Name).ShouldContain("Other Agent");
    }

    [Fact]
    public async Task LoadAsync_SpecificAgent_ReturnsDefinition()
    {
        // Arrange
        var agentDir = Path.Combine(_testDir, "researcher");
        Directory.CreateDirectory(agentDir);
        await File.WriteAllTextAsync(Path.Combine(agentDir, "AGENT.md"), """
            ---
            name: Research Agent
            provider: Anthropic
            model: claude-sonnet-4-20250514
            temperature: 0.5
            toolGroups: [web-search]
            ---
            You are a research specialist.
            """);

        // Act
        var result = await _provider.LoadAsync(_testDir, "researcher");

        // Assert
        result.ShouldNotBeNull();
        result.AgentId.ShouldBe("researcher");
        result.Name.ShouldBe("Research Agent");
        result.Provider.ShouldBe("Anthropic");
        result.Model.ShouldBe("claude-sonnet-4-20250514");
        result.Temperature.ShouldBe(0.5f);
        result.ToolGroups.ShouldBe(["web-search"]);
        result.Instructions.ShouldContain("research specialist");
        result.FilePath.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoadAsync_WithPersona_MergesContent()
    {
        // Arrange
        var agentDir = Path.Combine(_testDir, "persona-agent");
        Directory.CreateDirectory(agentDir);
        await File.WriteAllTextAsync(Path.Combine(agentDir, "AGENT.md"), """
            ---
            name: Persona Agent
            ---
            Do helpful things.
            """);
        await File.WriteAllTextAsync(Path.Combine(agentDir, "PERSONA.md"), """
            ---
            name: Friendly Bot
            tone: warm
            language: en
            ---
            You are always cheerful and encouraging.
            """);

        // Act
        var result = await _provider.LoadAsync(_testDir, "persona-agent");

        // Assert
        result.ShouldNotBeNull();
        result.Name.ShouldBe("Persona Agent");
        result.Instructions.ShouldContain("helpful things");
        result.PersonaContent.ShouldNotBeNull();
        result.PersonaContent.ShouldContain("cheerful and encouraging");
    }

    [Fact]
    public async Task LoadAsync_NonExistentAgent_ReturnsNull()
    {
        // Act
        var result = await _provider.LoadAsync(_testDir, "does-not-exist");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task DiscoverAsync_EmptyDir_ReturnsEmpty()
    {
        // Act
        var results = await _provider.DiscoverAsync(_testDir);

        // Assert
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_NonExistentDir_ReturnsEmpty()
    {
        // Act
        var results = await _provider.DiscoverAsync(Path.Combine(_testDir, "nonexistent"));

        // Assert
        results.ShouldBeEmpty();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
