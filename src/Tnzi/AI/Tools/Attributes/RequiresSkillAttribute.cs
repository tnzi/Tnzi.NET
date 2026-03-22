namespace Tnzi.AI.Tools.Attributes;

/// <summary>
/// 标记工具组或方法需要在使用前阅读指定 Skill。
/// 框架自动在工具描述中追加提示，引导 AI 在使用前调用 skill_get 阅读技能指南。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequiresSkillAttribute : Attribute
{
    /// <summary>
    /// 需要阅读的 Skill slug
    /// </summary>
    public string Slug { get; }

    /// <summary>
    /// 初始化 RequiresSkillAttribute
    /// </summary>
    /// <param name="slug">Skill slug（kebab-case 标识符）</param>
    public RequiresSkillAttribute(string slug)
    {
        Slug = Check.NotNullOrWhiteSpace(slug);
    }
}
