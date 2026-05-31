namespace Tnzi.AI.Rag.VectorStore;

/// <summary>
/// 多租户隔离的原生 SQL 片段组合器（B15）。
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
