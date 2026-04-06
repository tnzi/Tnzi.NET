namespace Tnzi.AI.Skills;

/// <summary>
/// 技能模板引擎实现 — 解析 {{param}} 占位符、验证参数值、渲染最终提示词
/// </summary>
public partial class SkillTemplateEngine : ISkillTemplateEngine
{
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex ParamRegex();

    /// <summary>
    /// Built-in variable names that are automatically resolved from context (not from user parameters).
    /// </summary>
    public static readonly HashSet<string> BuiltInVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "SESSION_ID",
        "AGENT_NAME"
    };

    /// <inheritdoc />
    public SkillRenderResult Render(SkillDefinition skill, Dictionary<string, string>? parameters = null)
    {
        return Render(skill, parameters, context: null);
    }

    /// <inheritdoc />
    public SkillRenderResult Render(SkillDefinition skill, Dictionary<string, string>? parameters, SkillRenderContext? context)
    {
        Check.NotNull(skill);

        var result = new SkillRenderResult();
        var provided = parameters ?? [];

        // 1. Extract placeholder names from the template
        var placeholderNames = ExtractParameterNames(skill.Content);

        // 2. Build a lookup of defined parameters (schema)
        var schema = skill.Parameters.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        // 3. Resolve effective values: provided > default; validate required + allowed values
        var effectiveValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 3a. Inject built-in variables from context
        if (context != null)
        {
            if (context.SessionId != null)
                effectiveValues["SESSION_ID"] = context.SessionId;
            if (context.AgentName != null)
                effectiveValues["AGENT_NAME"] = context.AgentName;
        }

        foreach (var paramDef in skill.Parameters)
        {
            if (provided.TryGetValue(paramDef.Name, out var suppliedValue))
            {
                // Validate allowed values
                if (paramDef.AllowedValues is { Count: > 0 }
                    && !paramDef.AllowedValues.Contains(suppliedValue, StringComparer.OrdinalIgnoreCase))
                {
                    result.Errors.Add(
                        $"Parameter '{paramDef.Name}' value '{suppliedValue}' is not allowed. " +
                        $"Allowed values: {string.Join(", ", paramDef.AllowedValues)}.");
                    continue;
                }

                effectiveValues[paramDef.Name] = suppliedValue;
            }
            else if (paramDef.DefaultValue != null)
            {
                effectiveValues[paramDef.Name] = paramDef.DefaultValue;
            }
            else if (paramDef.Required)
            {
                result.Errors.Add($"Required parameter '{paramDef.Name}' is missing.");
            }
        }

        // 4. Check for placeholders with no matching parameter definition (unresolved)
        //    Built-in variables are exempt from this check
        foreach (var placeholder in placeholderNames)
        {
            if (!schema.ContainsKey(placeholder) && !BuiltInVariables.Contains(placeholder))
            {
                result.Errors.Add($"Unresolved placeholder '{placeholder}': no matching parameter definition found.");
            }
        }

        // 5. Report unused parameters (provided but not referenced in template)
        foreach (var key in provided.Keys)
        {
            if (!placeholderNames.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                result.UnusedParameters.Add(key);
            }
        }

        // 6. If there are errors, return failure without rendering
        if (result.Errors.Count > 0)
        {
            result.Success = false;
            return result;
        }

        // 7. Replace {{param}} placeholders with resolved values
        var rendered = ParamRegex().Replace(skill.Content, match =>
        {
            var name = match.Groups[1].Value;
            return effectiveValues.TryGetValue(name, out var value) ? value : match.Value;
        });

        result.Success = true;
        result.RenderedContent = rendered;
        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ExtractParameterNames(string template)
    {
        Check.NotNull(template);

        if (string.IsNullOrEmpty(template))
            return [];

        return ParamRegex().Matches(template)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
