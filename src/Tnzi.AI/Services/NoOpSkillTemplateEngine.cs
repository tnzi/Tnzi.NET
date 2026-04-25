namespace Tnzi.AI.Services;

public class NoOpSkillTemplateEngine : ISkillTemplateEngine, INoOpService
{
    public SkillRenderResult Render(SkillDefinition skill, Dictionary<string, string>? parameters = null)
        => new()
        {
            Success = true,
            RenderedContent = skill.Content
        };

    public IReadOnlyList<string> ExtractParameterNames(string template)
        => Array.Empty<string>();
}
