using Tnzi.AI.Sandbox.Tools;

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
        researcher.ToolGroups.ShouldContain("websearch");
    }

    [Fact]
    public void Get_GeneralPurpose_ExcludesOrchestrationToolGroups()
    {
        var gp = _registry.Get("general-purpose");
        gp.ShouldNotBeNull();
        gp.ExcludedToolGroups.ShouldBe(["task", "clarification", "artifact"], ignoreOrder: true);
    }

    /// <summary>
    /// 内置类型引用的每个组名都必须是扫描器真的能登记出来的组：未知组名在注册表里静默解析为零个工具。
    /// 此前 general-purpose 写的是 default / file / code / web-search，researcher 写的是 web-search / file，
    /// 排除清单写的是 present-files —— 五个名字没有一个存在过。
    /// </summary>
    [Fact]
    public void BuiltInTypes_ReferenceOnlyToolGroupsTheScannerRegisters()
    {
        var scanner = new ToolScanner(NullLogger<ToolScanner>.Instance);
        var registeredGroups = new[] { typeof(AIModule).Assembly, typeof(SandboxTools).Assembly }
            .SelectMany(scanner.ScanAssembly)
            .Select(t => t.GroupName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in _registry.GetAll())
        {
            definition.ToolGroups.ShouldBeSubsetOf(registeredGroups, $"{definition.Name}.ToolGroups");
            definition.ExcludedToolGroups.ShouldBeSubsetOf(registeredGroups, $"{definition.Name}.ExcludedToolGroups");
        }
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
