using Tnzi.Modules;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// RagModule 单元测试 — 验证模块属性和配置
/// </summary>
public class RagModuleTests
{
    [Fact]
    public void LoadOrder_Is55()
    {
        var module = new RagModule();

        module.LoadOrder.ShouldBe(55);
    }

    [Fact]
    public void TableNamePrefix_IsRAG()
    {
        var module = new RagModule();

        module.TableNamePrefix.ShouldBe("RAG");
    }

    [Fact]
    public void DependsOn_AIModule()
    {
        var dependsOnAttrs = typeof(RagModule)
            .GetCustomAttributes(typeof(DependsOnAttribute), inherit: true)
            .Cast<DependsOnAttribute>()
            .ToList();

        dependsOnAttrs.ShouldNotBeEmpty();
        dependsOnAttrs.SelectMany(a => a.DependedModuleTypes).ShouldContain(typeof(AI.AIModule));
    }
}
