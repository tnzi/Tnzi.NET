using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// IToolRegistry.UnregisterTool 单工具移除测试
/// </summary>
public class ToolRegistryUnregisterTests
{
    private readonly ToolRegistry _registry;

    public ToolRegistryUnregisterTests()
    {
        var logger = NullLogger<ToolRegistry>.Instance;
        _registry = new ToolRegistry(logger);
    }

    [Fact]
    public void UnregisterTool_ExistingTool_RemovesFromNameIndex()
    {
        // Arrange
        var tool = CreateToolDefinition("my_tool", "group1");
        _registry.Register(tool);

        // Act
        var result = _registry.UnregisterTool("my_tool");

        // Assert
        result.ShouldBeTrue();
        _registry.GetAllTools().ShouldNotContain(t => t.Name == "my_tool");
    }

    [Fact]
    public void UnregisterTool_ExistingTool_RemovesFromGroupList()
    {
        // Arrange
        var tool1 = CreateToolDefinition("tool_a", "group1");
        var tool2 = CreateToolDefinition("tool_b", "group1");
        _registry.Register(tool1);
        _registry.Register(tool2);

        // Act
        _registry.UnregisterTool("tool_a");

        // Assert
        var groupTools = _registry.GetToolsByGroup("group1");
        groupTools.ShouldNotContain(t => t.Name == "tool_a");
        groupTools.ShouldContain(t => t.Name == "tool_b");
    }

    [Fact]
    public void UnregisterTool_LastToolInGroup_RemovesEmptyGroup()
    {
        // Arrange
        var tool = CreateToolDefinition("only_tool", "lonely_group");
        _registry.Register(tool);

        // Act
        _registry.UnregisterTool("only_tool");

        // Assert
        _registry.GetAllGroupNames().ShouldNotContain("lonely_group");
    }

    [Fact]
    public void UnregisterTool_NonExistentTool_ReturnsFalse()
    {
        // Act
        var result = _registry.UnregisterTool("non_existent");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void UnregisterTool_NullName_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => _registry.UnregisterTool(null!));
    }

    [Fact]
    public void UnregisterTool_EmptyName_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => _registry.UnregisterTool(""));
    }

    [Fact]
    public void UnregisterTool_DoesNotAffectOtherGroups()
    {
        // Arrange
        var tool1 = CreateToolDefinition("tool_x", "group_a");
        var tool2 = CreateToolDefinition("tool_y", "group_b");
        _registry.Register(tool1);
        _registry.Register(tool2);

        // Act
        _registry.UnregisterTool("tool_x");

        // Assert
        _registry.GetToolsByGroup("group_b").Count.ShouldBe(1);
        _registry.GetToolsByGroup("group_b")[0].Name.ShouldBe("tool_y");
    }

    private static ToolDefinition CreateToolDefinition(string name, string groupName)
    {
        return new ToolDefinition
        {
            Name = name,
            GroupName = groupName,
            Description = $"Test tool {name}",
            ProviderType = typeof(ToolRegistryUnregisterTests),
            MethodInfo = typeof(ToolRegistryUnregisterTests).GetMethod(nameof(CreateToolDefinition),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
        };
    }
}
