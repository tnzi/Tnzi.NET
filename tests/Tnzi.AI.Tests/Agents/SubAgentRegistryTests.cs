namespace Tnzi.AI.Tests.Agents;

public class SubAgentRegistryTests
{
    private readonly ISubAgentRegistry _registry;

    public SubAgentRegistryTests()
    {
        _registry = new SubAgentRegistry();
    }

    [Fact]
    public void GetAll_ReturnsThreeBuiltInTypes()
    {
        var types = _registry.GetAll();
        types.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("general-purpose")]
    [InlineData("bash")]
    [InlineData("researcher")]
    public void Get_BuiltInType_ReturnsDefinition(string typeName)
    {
        var definition = _registry.Get(typeName);
        definition.ShouldNotBeNull();
        definition.Name.ShouldBe(typeName);
    }

    [Fact]
    public void Get_GeneralPurpose_Has50MaxTurns()
    {
        var gp = _registry.Get("general-purpose");
        gp.ShouldNotBeNull();
        gp.MaxTurns.ShouldBe(50);
    }

    [Fact]
    public void Get_Bash_Has30MaxTurnsAndSandboxTools()
    {
        var bash = _registry.Get("bash");
        bash.ShouldNotBeNull();
        bash.MaxTurns.ShouldBe(30);
        bash.ToolGroups.ShouldContain("sandbox");
    }

    [Fact]
    public void Get_Researcher_Has30MaxTurnsAndWebSearchTools()
    {
        var researcher = _registry.Get("researcher");
        researcher.ShouldNotBeNull();
        researcher.MaxTurns.ShouldBe(30);
        researcher.ToolGroups.ShouldContain("web-search");
        researcher.ToolGroups.ShouldContain("file");
    }

    [Fact]
    public void Get_GeneralPurpose_HasExcludedToolGroups()
    {
        var gp = _registry.Get("general-purpose");
        gp.ShouldNotBeNull();
        gp.ExcludedToolGroups.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Register_CustomType_CanRetrieve()
    {
        var custom = new SubAgentTypeDefinition(
            Name: "code-reviewer",
            Description: "Reviews code for quality",
            ToolGroups: ["code-analysis", "file"],
            ExcludedToolGroups: [],
            MaxTurns: 20,
            Instructions: "Review code carefully.");

        _registry.Register(custom);

        var retrieved = _registry.Get("code-reviewer");
        retrieved.ShouldNotBeNull();
        retrieved.Name.ShouldBe("code-reviewer");
        retrieved.MaxTurns.ShouldBe(20);
    }

    [Fact]
    public void Register_OverrideBuiltIn_ReplacesDefinition()
    {
        var customBash = new SubAgentTypeDefinition(
            Name: "bash",
            Description: "Custom bash agent",
            ToolGroups: ["sandbox", "custom-tool"],
            ExcludedToolGroups: [],
            MaxTurns: 10,
            Instructions: "Custom bash instructions.");

        _registry.Register(customBash);

        var retrieved = _registry.Get("bash");
        retrieved.ShouldNotBeNull();
        retrieved.MaxTurns.ShouldBe(10);
        retrieved.ToolGroups.ShouldContain("custom-tool");
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        var result = _registry.Get("nonexistent");
        result.ShouldBeNull();
    }

    [Fact]
    public void Unregister_ExistingType_RemovesIt()
    {
        _registry.Register(new SubAgentTypeDefinition(
            Name: "temp", Description: "Temp", ToolGroups: [], ExcludedToolGroups: [], MaxTurns: 5));

        _registry.Get("temp").ShouldNotBeNull();

        var removed = _registry.Unregister("temp");
        removed.ShouldBeTrue();
        _registry.Get("temp").ShouldBeNull();
    }

    [Fact]
    public void Unregister_NonExistent_ReturnsFalse()
    {
        var removed = _registry.Unregister("nonexistent");
        removed.ShouldBeFalse();
    }
}
