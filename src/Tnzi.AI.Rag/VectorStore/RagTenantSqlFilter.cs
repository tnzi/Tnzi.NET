namespace Tnzi.AI.Rag.VectorStore;

/// <summary>
/// 多租户隔离的原生 SQL 片段组合器（B15）+ 跨库检索的租户上下文强制点。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PgVectorStore"/> 和 <see cref="Search.PgFullTextSearchProvider"/> 走的是手写 Npgsql，
/// 绕过了 EF Core 的全局租户过滤器。在多租户部署下，跨知识库 / 无过滤搜索会返回其他租户的 chunk。
/// 本组合器在 <b>当前存在非空租户上下文</b>（多租户启用且 <c>ICurrentTenant.Id</c> 非空）时，
/// 才追加 <c>AND "TenantId" = @tenantId</c> 谓词。
/// </para>
/// <para>
/// 单租户 / host 场景下（<c>tenantId</c> 为 null）<b>不追加任何谓词</b>：消费方的表里可能根本没有
/// <c>TenantId</c> 列（框架的 <c>InitRag</c> 迁移不含该列），无条件追加会导致
/// <c>column "TenantId" does not exist</c> 从而破坏所有查询。
/// </para>
/// <para>
/// 上述"null 即放行"的设计在多租户部署下有一个例外必须封死：host 上下文（后台任务等
/// <c>ICurrentTenant.Id</c> 为 null 的场景）发起 <b>无知识库范围限定</b> 的跨库检索（search-all）时，
/// 不追加谓词意味着返回全部租户的数据 = 泄漏。<see cref="EnsureTenantScopeForSearchAll"/>
/// 是该场景的单点 fail-closed 强制点，Pg（<see cref="PgVectorStore"/>、
/// <see cref="Search.PgFullTextSearchProvider"/>）与 <see cref="InMemoryVectorStore"/> 两路实现共用。
/// </para>
/// </remarks>
public static class RagTenantSqlFilter
{
    /// <summary>租户参数名（不含 <c>@</c> 前缀）。</summary>
    public const string TenantParameterName = "tenantId";

    /// <summary>
    /// 构建租户隔离的 WHERE 片段。
    /// </summary>
    /// <param name="tenantId">当前租户 ID；null 表示单租户 / host，不做租户过滤。</param>
    /// <param name="columnQualifier">列限定前缀（如 join 查询中的 <c>"c."</c>）；非 join 查询传空串。</param>
    /// <returns>
    /// 当 <paramref name="tenantId"/> 非空时返回形如 <c> AND c."TenantId" = @tenantId</c> 的片段，
    /// 否则返回空字符串。
    /// </returns>
    public static string BuildPredicate(Guid? tenantId, string columnQualifier = "")
    {
        if (tenantId is null)
        {
            return string.Empty;
        }

        return $" AND {columnQualifier}\"TenantId\" = @{TenantParameterName}";
    }

    /// <summary>
    /// 跨库检索（search-all，即未限定知识库范围）的租户上下文 fail-closed 守卫。
    /// <para>
    /// 多租户启用而当前无租户上下文时，无 KB 范围限定的跨库检索会绕过租户谓词、
    /// 返回全部租户的 chunk —— 必须显式拒绝而非静默放行。带明确 KB 范围的查询不受影响
    /// （KB 本身经 EF 全局过滤器按租户解析）。
    /// </para>
    /// </summary>
    /// <param name="multiTenancyEnabled">多租户是否启用（<c>MultiTenancyOptions.Enabled</c>）。</param>
    /// <param name="tenantId">当前租户 ID（null 表示 host / 无租户上下文）。</param>
    /// <param name="hasKnowledgeBaseScope">本次查询是否限定了明确的知识库范围。</param>
    /// <exception cref="InvalidOperationException">
    /// 多租户启用 + 无租户上下文 + 无知识库范围限定时抛出。
    /// </exception>
    public static void EnsureTenantScopeForSearchAll(bool multiTenancyEnabled, Guid? tenantId, bool hasKnowledgeBaseScope)
    {
        if (multiTenancyEnabled && tenantId is null && !hasKnowledgeBaseScope)
        {
            throw new InvalidOperationException(
                "Cross-knowledge-base RAG search requires a tenant context when multi-tenancy is enabled: "
                + "searching without one would return chunks from all tenants. "
                + "Establish a tenant context (e.g. via ICurrentTenant.Change) or scope the search to explicit knowledge base ids.");
        }
    }

    /// <summary>
    /// 向命令追加租户参数（仅当 <paramref name="tenantId"/> 非空）。
    /// </summary>
    public static void AddParameter(NpgsqlCommand command, Guid? tenantId)
    {
        Check.NotNull(command);

        if (tenantId is { } value)
        {
            command.Parameters.Add(new NpgsqlParameter(TenantParameterName, NpgsqlDbType.Uuid) { Value = value });
        }
    }
}
