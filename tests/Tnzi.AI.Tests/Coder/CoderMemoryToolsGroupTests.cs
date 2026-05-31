using Tnzi.AI.Tools.Attributes;
using CoderMemoryTools = Tnzi.AI.Coder.Memory.MemoryTools;

namespace Tnzi.AI.Tests.Coder;

/// <summary>
/// C9: Coder MemoryTools must use distinct 'coder-memory' group (not 'memory' which collides with core).
/// </summary>
public class CoderMemoryToolsGroupTests
{
    [Fact]
    public void CoderMemoryTools_HasDistinctToolGroup()
    {
        var attr = typeof(CoderMemoryTools)
            .GetCustomAttributes(typeof(AIToolGroupAttribute), inherit: false)
            .Cast<AIToolGroupAttribute>()
            .SingleOrDefault();

        attr.ShouldNotBeNull();
        attr.GroupName.ShouldBe("coder-memory");
    }

    [Fact]
    public void CoderOptions_DefaultToolGroups_ContainsCoderMemoryNotMemory()
    {
        var options = new CoderOptions();

        options.DefaultToolGroups.ShouldContain("coder-memory");
        options.DefaultToolGroups.ShouldNotContain("memory");
    }
}
