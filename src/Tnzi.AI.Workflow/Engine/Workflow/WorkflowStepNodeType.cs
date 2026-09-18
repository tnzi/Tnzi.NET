namespace Tnzi.AI.Workflow.Engine;

/// <summary>
/// 步骤节点类型在 <see cref="WorkflowStepDto.Configuration"/> 里的键，以及唯一的一处读取逻辑。
/// </summary>
/// <remarks>
/// <para>
/// 运行时契约是 <c>Configuration["nodeType"]</c>（<see cref="WorkflowNodeTypes"/> 常量，忽略大小写）：
/// <see cref="WorkflowNodeExecutor"/> 据此解析 <see cref="IWorkflowNode"/> 实现，缺键回退到 agent 节点。
/// </para>
/// <para>
/// ★ 可视化编辑器 2026-05-18 至 2026-09-12 间把类型写在 <c>__nodeType</c> 下，而后端从未读过那个键：
/// 编辑器里选的 router / parallel / debate / … 全部以 agent 执行且状态 Completed，没有任何症状。
/// 编辑器现已改写 <c>nodeType</c>；这里把旧键当作只读的回退，让消费方数据库里已保存的定义
/// 不必重新保存就按真实类型运行。读取集中在一处，否则四个读取点里漏掉一个就是同一个缺陷再来一次。
/// </para>
/// </remarks>
public static class WorkflowStepNodeType
{
    /// <summary>运行时契约键。</summary>
    public const string ConfigurationKey = "nodeType";

    /// <summary>旧编辑器写出的键。只读；<see cref="Normalize"/> 会把它折成 <see cref="ConfigurationKey"/>。</summary>
    public const string LegacyEditorKey = "__nodeType";

    /// <summary>
    /// 读取步骤声明的节点类型：优先 <c>nodeType</c>，缺失时回退 <c>__nodeType</c>；两者都没有（或为空白）返回 null。
    /// </summary>
    public static string? Get(WorkflowStepDto step)
    {
        Check.NotNull(step);
        return Get(step.Configuration);
    }

    /// <summary>
    /// 在配置字典上就地规范化：<c>nodeType</c> 缺失而 <c>__nodeType</c> 存在时复制过去，然后删除旧键。
    /// 供图构建时的步骤克隆调用，之后引擎与执行器只会看到 <c>nodeType</c>。
    /// </summary>
    public static void Normalize(Dictionary<string, string> configuration)
    {
        Check.NotNull(configuration);

        if (!configuration.TryGetValue(LegacyEditorKey, out var legacy))
            return;

        if (!HasValue(configuration, ConfigurationKey) && !string.IsNullOrWhiteSpace(legacy))
        {
            configuration[ConfigurationKey] = legacy;
        }

        configuration.Remove(LegacyEditorKey);
    }

    private static string? Get(IReadOnlyDictionary<string, string>? configuration)
    {
        if (configuration == null)
            return null;

        if (configuration.TryGetValue(ConfigurationKey, out var nodeType) && !string.IsNullOrWhiteSpace(nodeType))
            return nodeType;

        if (configuration.TryGetValue(LegacyEditorKey, out var legacy) && !string.IsNullOrWhiteSpace(legacy))
            return legacy;

        return null;
    }

    private static bool HasValue(Dictionary<string, string> configuration, string key)
        => configuration.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
}
