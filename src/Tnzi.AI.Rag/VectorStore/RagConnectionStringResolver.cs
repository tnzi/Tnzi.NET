namespace Tnzi.AI.Rag.VectorStore;

/// <summary>
/// 解析 pgvector 直连 Npgsql 组件（<see cref="PgVectorStore"/> /
/// <see cref="Search.PgFullTextSearchProvider"/>）使用的数据库连接字符串。
/// </summary>
/// <remarks>
/// <para>
/// 这些组件走手写 Npgsql（不经 EF），检索与删除的就是摄取经 <see cref="RagDbContext"/> 写入的那张分块表，
/// 所以它们必须连 <see cref="RagDbContext"/> 连的那个库。解析顺序：
/// </para>
/// <list type="number">
/// <item>框架 <c>Database:DbContexts</c> 段中 <see cref="RagDbContext"/> 的条目：<c>DbContextType</c> 指向它，
/// 或不写 <c>DbContextType</c>、按 <c>Name</c> 自动发现到它（<c>"Rag"</c>）。连接串按 EF 侧同一规则得出：
/// 展开 <c>${VAR}</c> / <c>$(VAR)</c> 占位符（解析不出就拒绝并指名变量）、套上该条目的连接池配置。</item>
/// <item>没有这样的条目（或条目没写连接串）时回退到 <c>ConnectionStrings:Default</c>。</item>
/// </list>
/// <para>
/// ★ 此前顺序相反：只要宿主有一条 <c>ConnectionStrings:Default</c>（健康检查、后台任务常用），就用它而不看 RAG 自己的条目。
/// RAG 按文档单独指向一个库时，摄取写进 RAG 库，检索与删除却打开另一个库：检索被吞成「没有相关信息」，
/// 删除文档 / 知识库 500 或删错库、分块永远留在 RAG 库里。两者指向同一个库的部署不受影响。
/// </para>
/// <para>
/// 读取的是配置里的<b>原始</b>连接串，而非 <c>RagDbContext.Database.GetConnectionString()</c>：
/// 后者在 Npgsql 连接打开后会因 <c>Persist Security Info=false</c>（默认）剥离密码。
/// </para>
/// </remarks>
internal static class RagConnectionStringResolver
{
    /// <summary>不写 <c>DbContextType</c> 时，EF 模块按「{Name}DbContext」自动发现到 <see cref="RagDbContext"/> 的条目名。</summary>
    private const string DiscoveredByName = "Rag";

    public static string Resolve(IConfiguration configuration, string componentName)
    {
        Check.NotNull(configuration);

        var ragContextTypeName = typeof(RagDbContext).FullName!;
        foreach (var section in configuration.GetSection("Database:DbContexts").GetChildren())
        {
            var entry = section.Get<DbContextConfiguration>();
            if (entry is null || string.IsNullOrWhiteSpace(entry.ConnectionString) || !IsRagEntry(entry, ragContextTypeName))
            {
                continue;
            }

            return entry.GetEffectiveConnectionString(configuration);
        }

        var connectionString = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        throw new InvalidOperationException(
            $"{componentName} could not resolve a database connection string. Configure a RagDbContext entry with a " +
            "ConnectionString under 'Database:DbContexts', or 'ConnectionStrings:Default'.");
    }

    private static bool IsRagEntry(DbContextConfiguration entry, string ragContextTypeName)
        => string.IsNullOrWhiteSpace(entry.DbContextType)
            ? string.Equals(entry.Name, DiscoveredByName, StringComparison.OrdinalIgnoreCase)
            : entry.DbContextType.Contains(ragContextTypeName, StringComparison.Ordinal);
}
