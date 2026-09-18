namespace Tnzi.AI.Skills;

/// <summary>
/// 当前请求作用域内「已激活技能」的唯一真值源，并负责把这个集合在会话线程上跨轮次保存。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么必须存在</b>：技能约束（工具组 / 单工具白黑名单 / 模型与 Provider 覆盖）此前的
/// 传播链是 <c>SkillContextProvider._activatedSkills</c> → <c>ContextInjection.ActiveSkills</c> →
/// <c>Properties["ActiveSkills"]</c> → <c>SkillConstraintMiddleware</c>。这条链上每一环都在
/// <c>skill_activate</c> 真正被模型调用<b>之前</b>就已经走完（Provider 按请求新建，
/// 中间件 400/450 都在 agent 循环之前执行；<c>CompositeContextProvider</c> 合并时还会把
/// <c>ActiveSkills</c> 丢掉），于是约束在任何一次真实请求上都没有执行过。
/// </para>
/// <para>
/// 现在：<c>skill_activate</c> / <c>skill_deactivate</c> 直接写本跟踪器；
/// <c>SkillConstraintToolMiddleware</c>（工具执行管线）在<b>每一次工具调用</b>时读它，
/// 于是激活当轮就生效；<c>SkillConstraintMiddleware</c>（Order 450）在<b>下一轮开始</b>时
/// 读它，用于从模型可见的工具列表里摘掉被禁工具并应用模型 / Provider 覆盖。
/// 跨轮次靠 <see cref="RestoreAsync"/> / <see cref="PersistAsync"/> 经线程元数据往返。
/// </para>
/// <para>
/// 生命周期 Scoped：一个 HTTP 请求（一轮对话）一个实例。子 agent（<c>spawn_agent</c>）在
/// 新作用域里跑，不继承父 agent 的激活集。
/// </para>
/// </remarks>
public interface ISkillActivationTracker
{
    /// <summary>当前作用域内已激活的技能（含从线程恢复的与本轮新激活的）。</summary>
    IReadOnlyList<SkillDefinition> ActivatedSkills { get; }

    /// <summary>指定 slug 是否已激活（不区分大小写）。</summary>
    bool IsActivated(string slug);

    /// <summary>激活技能；同 slug 已存在时替换（参数不同的再次激活）。</summary>
    void Activate(SkillDefinition skill);

    /// <summary>取消激活；返回是否真的移除了一条。</summary>
    bool Deactivate(string slug);

    /// <summary>
    /// 从会话线程恢复上一轮结束时的激活集。每个作用域只真正执行一次（幂等），
    /// 解析不到的 slug（技能已删除 / 禁用）静默跳过。
    /// </summary>
    /// <remarks>
    /// 读取失败（线程存储不可用 / 元数据损坏）必须<b>抛出且不记为已恢复</b>：同作用域的下一个调用方会再试。
    /// 这一步不能只挂在内容注入上 —— <c>CompositeContextProvider</c> 会按 token 预算跳过 Skills provider、
    /// agent 配置也能整个关掉上下文注入，所以 <c>SkillConstraintMiddleware</c>（450）在每轮开始时也调它，
    /// 无论内容注入那一步跑没跑过。
    /// </remarks>
    Task RestoreAsync(Guid threadId, CancellationToken ct = default);

    /// <summary>把当前激活集写回会话线程，供下一轮 <see cref="RestoreAsync"/> 读取。</summary>
    Task PersistAsync(Guid threadId, CancellationToken ct = default);
}
