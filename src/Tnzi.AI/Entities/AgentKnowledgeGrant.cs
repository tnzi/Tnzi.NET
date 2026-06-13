namespace Tnzi.AI.Entities;

/// <summary>
/// Agent ↔ 知识库授权 junction 实体。把一个 Agent 与一个知识库（按 Id）关联起来，
/// 取代 <see cref="Agent"/> 原先的 KnowledgeBaseIds JSON 列。FK 仅指向同程序集的 <see cref="Agent"/>。
/// Agent-to-knowledge-base grant junction — associates an Agent with a knowledge base (by Id),
/// replacing <see cref="Agent"/>'s former KnowledgeBaseIds JSON column. FK targets the same-assembly
/// <see cref="Agent"/> only.
/// </summary>
public class AgentKnowledgeGrant : MultiTenantAuditedEntity<Guid>, IGrantEnableState
{
    /// <summary>被授权的 Agent。The Agent this grant belongs to.</summary>
    public Guid AgentId { get; set; }

    /// <summary>导航到所属 Agent。Navigation to the owning Agent.</summary>
    public virtual Agent? Agent { get; set; }

    /// <summary>
    /// 知识库 Id（裸 Guid，<b>无导航属性</b>）。
    /// <c>KnowledgeBase</c> 位于 <c>Tnzi.AI.Rag</c> 程序集（该程序集依赖 AIModule）；
    /// 在此加反向外键会倒置模块依赖方向（禁止），故按值引用。
    /// Knowledge base Id (bare Guid, <b>no navigation</b>). <c>KnowledgeBase</c> lives in the
    /// <c>Tnzi.AI.Rag</c> assembly (which depends on AIModule); a back-FK here would invert the
    /// module dependency (forbidden), so it is referenced by value.
    /// </summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>是否启用本条授权。Whether this grant is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>优先级（越大越优先）。Priority (higher wins).</summary>
    public int Priority { get; set; }
}
