namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// Skill Inline/Fork 双模式执行测试
/// </summary>
public class SkillExecutionModeTests
{
    [Fact]
    public void SkillDefinition_DefaultExecutionContext_IsInline()
    {
        var skill = new SkillDefinition();
        skill.ExecutionContext.ShouldBe(SkillExecutionContext.Inline);
    }

    [Fact]
    public void SkillDefinition_ForkMode_CanBeSet()
    {
        var skill = new SkillDefinition
        {
            Slug = "heavy-analysis",
            ExecutionContext = SkillExecutionContext.Fork
        };
        skill.ExecutionContext.ShouldBe(SkillExecutionContext.Fork);
    }

    [Fact]
    public void SkillExecutionContext_EnumValues_AreCorrect()
    {
        ((int)SkillExecutionContext.Inline).ShouldBe(0);
        ((int)SkillExecutionContext.Fork).ShouldBe(1);
    }
}
