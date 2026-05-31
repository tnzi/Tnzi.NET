namespace Tnzi.AI.Rag.VectorStore;

/// <summary>
/// 解析 pgvector 直连 Npgsql 组件（<see cref="PgVectorStore"/> /
/// <see cref="Search.PgFullTextSearchProvider"/>）使用的数据库连接字符串。
/// </summary>
/// <remarks>
/// <para>
/// 这些组件走手写 Npgsql（不经 EF），需要独立的连接字符串。解析顺序：
/// </para>
/// <list type="number">
/// <item><c>ConnectionStrings:Default</c>（向后兼容，历史默认来源）。</item>
/// <item>回退到框架 <c>Database:DbContexts</c> 段中 <see cref="RagDbContext"/> 配置的连接串 ——
/// 遵循框架标准库配置约定，使用 <c>Database:DbContexts</c> 配置 RagDbContext 的应用<b>无需</b>
/// 再单独配置 <c>ConnectionStrings:Default</c>（消除"启用 RAG 必须额外配一份连接串"的配置陷阱）。</item>
/// </list>
/// <para>
/// 回退时直接读取配置的<b>原始</b>连接串，而非 <c>RagDbContext.Database.GetConnectionString()</c> ——
/// 后者在 Npgsql 连接打开后会因 <c>Persist Security Info=false</c>（默认）剥离密码，导致回退串缺密码。
/// </para>
/// </remarks>
internal static class RagConnectionStringResolver
{
    public static string Resolve(IConfiguration configuration, string componentName)
    {
        Check.NotNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        // 回退：从 Database:DbContexts 段读 RagDbContext 条目的原始连接串（含密码）。
        var ragContextTypeName = typeof(RagDbContext).FullName!;
        foreach (var ctx in configuration.GetSection("Database:DbContexts").GetChildren())
        {
            var dbContextType = ctx["DbContextType"];
            if (!string.IsNullOrWhiteSpace(dbContextType)
                && dbContextType.Contains(ragContextTypeName, StringComparison.Ordinal))
            {
                var cs = ctx["ConnectionString"];
                if (!string.IsNullOrWhiteSpace(cs))
                {
                    return cs;
                }
            }
        }

        throw new InvalidOperationException(
            $"{componentName} could not resolve a database connection string. Configure either " +
            "'ConnectionStrings:Default' or a RagDbContext entry with a ConnectionString under 'Database:DbContexts'.");
    }
}
