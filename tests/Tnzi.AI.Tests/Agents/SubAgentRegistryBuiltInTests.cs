namespace Tnzi.AI.Tests.Agents;

/// <summary>
/// 内置子 Agent 类型的系统提示词与能力标签必须与它真拿得到的工具组一致：
/// 提示词是模型真正看到的文本（Description 不是），让模型去「读文件」而没有文件工具，
/// 结果不是幻觉出一次读取就是答「我没有文件工具」。
/// </summary>
public class SubAgentRegistryBuiltInTests
{
    [Fact]
    public void Researcher_DoesNotClaimFileAccessItHasNoToolFor()
    {
        var researcher = new SubAgentRegistry().Get("researcher");

        researcher.ShouldNotBeNull();
        researcher!.ToolGroups.ShouldBe(["websearch"]);
        researcher.Instructions.ShouldNotBeNull().ShouldNotContain("file", Case.Insensitive);
        researcher.CapabilityTags.ShouldNotBeNull().ShouldNotContain("files");
    }
}
