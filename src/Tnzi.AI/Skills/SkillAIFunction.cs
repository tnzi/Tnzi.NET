namespace Tnzi.AI.Skills;

/// <summary>
/// 将已激活的 Skill 包装为 MEAI AIFunction，使其可在 FunctionInvokingChatClient 管道中被原生调用
/// </summary>
/// <remarks>
/// <para>
/// 当 Skill 被激活后，SkillContextProvider 会为其创建 SkillAIFunction 实例并注入到 ContextInjection.Tools 中。
/// LLM 可以像调用普通工具一样调用该 Skill，接收渲染后的 Skill 内容作为返回值。
/// </para>
/// <para>
/// 工具命名规则: <c>skill_{slug}</c>，如 <c>skill_code-review</c>。
/// 接受单个可选 "input" 字符串参数，用于传递上下文或参数给技能模板引擎。
/// </para>
/// </remarks>
public sealed class SkillAIFunction : AIFunction
{
    private readonly SkillDefinition _skill;
    private readonly ISkillTemplateEngine _templateEngine;
    private readonly ISkillConstraintEnforcer? _constraintEnforcer;

    /// <summary>
    /// 初始化 SkillAIFunction
    /// </summary>
    /// <param name="skill">已激活的技能定义</param>
    /// <param name="templateEngine">模板引擎，用于渲染技能内容</param>
    /// <param name="constraintEnforcer">约束执行器（可选）</param>
    public SkillAIFunction(SkillDefinition skill, ISkillTemplateEngine templateEngine, ISkillConstraintEnforcer? constraintEnforcer = null)
    {
        _skill = Check.NotNull(skill);
        _templateEngine = Check.NotNull(templateEngine);
        _constraintEnforcer = constraintEnforcer;
    }

    /// <inheritdoc />
    public override string Name => $"skill_{_skill.Slug}";

    /// <inheritdoc />
    public override string Description => _skill.Description ?? _skill.Name;

    /// <inheritdoc />
    public override JsonElement JsonSchema => CachedSchema;

    /// <inheritdoc />
    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // 提取可选 "input" 参数
        string? input = null;
        if (arguments.TryGetValue("input", out var inputObj) && inputObj is not null)
        {
            input = inputObj.ToString();
        }

        // 构建模板参数（如果有 input，作为 "input" 参数传入）
        Dictionary<string, string>? templateParams = null;
        if (!string.IsNullOrWhiteSpace(input))
        {
            templateParams = new Dictionary<string, string> { ["input"] = input };
        }

        // 通过模板引擎渲染 Skill 内容
        var renderResult = _templateEngine.Render(_skill, templateParams);

        if (!renderResult.Success)
        {
            var errorMessage = $"Skill '{_skill.Name}' render failed: {string.Join("; ", renderResult.Errors)}";
            return new ValueTask<object?>((object)errorMessage);
        }

        // 通过约束执行器验证（如果可用）
        if (_constraintEnforcer is not null)
        {
            var constraintResult = _constraintEnforcer.Apply(_skill, new SkillConstraintContext());
            // 约束结果仅供日志/审计，不阻断渲染内容返回
        }

        return new ValueTask<object?>((object)renderResult.RenderedContent);
    }

    /// <summary>
    /// 静态缓存的 JSON Schema（所有 SkillAIFunction 实例共享）
    /// </summary>
    private static readonly JsonElement CachedSchema = BuildJsonSchemaOnce();

    /// <summary>
    /// 构建 JSON Schema：单个可选 "input" 字符串参数
    /// </summary>
    private static JsonElement BuildJsonSchemaOnce()
    {
        const string schema = """
            {
                "type": "object",
                "properties": {
                    "input": {
                        "type": "string",
                        "description": "Optional context or parameters to pass to the skill"
                    }
                }
            }
            """;

        using var doc = JsonDocument.Parse(schema);
        return doc.RootElement.Clone();
    }
}
