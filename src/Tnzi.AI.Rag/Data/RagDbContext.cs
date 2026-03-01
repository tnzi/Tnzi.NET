namespace Tnzi.AI.Rag.Data;

/// <summary>
/// RAG 模块独立 DbContext — 使用 PostgreSQL + pgvector
/// </summary>
/// <remarks>
/// 实体通过 EntityTypeConfigurationBase.DbContextType 自动绑定到此 DbContext，
/// 由 TnziDbContextHelper.OnModelCreating 统一注册，无需手动声明 DbSet。
/// 应用层需在 appsettings.json 的 Database:DbContexts 中配置此 DbContext。
/// </remarks>
public class RagDbContext : TnziDbContext<RagDbContext>
{
    public RagDbContext(
        DbContextOptions<RagDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null,
        TimeProvider? timeProvider = null)
        : base(options, currentUser, currentTenant, dataFilterManager, timeProvider)
    {
    }
}
