namespace Tnzi.AI.Skills;

/// <summary>
/// <see cref="ISkillActivationTracker"/> 默认实现：作用域内内存集合 + 经
/// <see cref="IAgentThreadInternalService"/> 把 slug 列表存进 <c>AgentThread.Metadata</c>
/// （键 <see cref="ThreadMetadataKey"/>）。
/// </summary>
/// <remarks>
/// 两个依赖都可空：无线程服务时只在内存里工作（单元测试 / 无线程的一次性运行），
/// 无技能注册表时（Skills 子模块未加载）<see cref="RestoreAsync"/> 恢复不出任何定义。
/// 持久化失败只记日志不抛：激活集丢失的后果是「下一轮少约束」而不是「本轮失败」，
/// 但本轮的内存集合仍然完整，当轮约束不受影响。
/// <b>恢复失败则相反</b>：读不到激活集就不知道本轮该套什么约束，<see cref="RestoreAsync"/> 抛出并且不标记已恢复，
/// 由调用方决定本轮能不能继续（<c>SkillConstraintMiddleware</c> 让本轮失败）。
/// </remarks>
public sealed class SkillActivationTracker : ISkillActivationTracker
{
    /// <summary><c>AgentThread.Metadata</c> 里存放已激活 slug 数组的键。</summary>
    public const string ThreadMetadataKey = "activatedSkills";

    private readonly IAgentThreadInternalService? _threadService;
    private readonly ISkillRegistry? _registry;
    private readonly ILogger<SkillActivationTracker> _logger;

    private readonly object _lock = new();
    private readonly List<SkillDefinition> _activated = [];
    private Guid? _restoredThreadId;

    public SkillActivationTracker(
        ILogger<SkillActivationTracker>? logger = null,
        IAgentThreadInternalService? threadService = null,
        ISkillRegistry? registry = null)
    {
        _logger = logger ?? NullLogger<SkillActivationTracker>.Instance;
        _threadService = threadService;
        _registry = registry;
    }

    /// <inheritdoc />
    public IReadOnlyList<SkillDefinition> ActivatedSkills
    {
        get
        {
            lock (_lock)
                return [.. _activated];
        }
    }

    /// <inheritdoc />
    public bool IsActivated(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return false;
        lock (_lock)
            return _activated.Any(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public void Activate(SkillDefinition skill)
    {
        Check.NotNull(skill);
        lock (_lock)
        {
            _activated.RemoveAll(s => string.Equals(s.Slug, skill.Slug, StringComparison.OrdinalIgnoreCase));
            _activated.Add(skill);
        }
    }

    /// <inheritdoc />
    public bool Deactivate(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return false;
        lock (_lock)
            return _activated.RemoveAll(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    /// <inheritdoc />
    public async Task RestoreAsync(Guid threadId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_restoredThreadId == threadId) return;
        }

        if (_threadService == null || _registry == null)
        {
            MarkRestored(threadId);
            return;
        }

        // 读失败（存储不可用 / 元数据损坏 / 注册表解析中途出错）直接抛，且**不**标记已恢复：
        // 吞掉它等于「本轮无约束运行」而唯一痕迹是一条日志；不标记则同作用域的下一个调用方
        // （450 中间件）会再读一次，仍失败就让本轮失败 —— 失败方向是关闭不是放行。
        // 只在整套激活集都解析完之后才标记：中途标记会让一次被吞掉的注册表异常留下「半套约束」而无人重试。
        var json = await _threadService.GetMetadataValueAsync(threadId, ThreadMetadataKey, ct);
        var slugs = string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];

        foreach (var slug in slugs.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            if (IsActivated(slug)) continue;

            var skill = await _registry.GetBySlugAsync(slug, ct);
            if (skill == null)
            {
                _logger.LogInformation("Activated skill '{Slug}' on thread {ThreadId} no longer resolves; dropping it", slug, threadId);
                continue;
            }

            Activate(skill);
        }

        MarkRestored(threadId);
    }

    private void MarkRestored(Guid threadId)
    {
        lock (_lock)
            _restoredThreadId = threadId;
    }

    /// <inheritdoc />
    public async Task PersistAsync(Guid threadId, CancellationToken ct = default)
    {
        if (_threadService == null) return;

        string[] slugs;
        lock (_lock)
            slugs = [.. _activated.Select(s => s.Slug)];

        try
        {
            var json = slugs.Length == 0 ? null : JsonSerializer.Serialize(slugs);
            await _threadService.SetMetadataValueAsync(threadId, ThreadMetadataKey, json, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to persist {Count} activated skills for thread {ThreadId}; constraints will not carry into the next turn", slugs.Length, threadId);
        }
    }
}
