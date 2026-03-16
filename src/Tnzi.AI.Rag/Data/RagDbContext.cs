using Pgvector.EntityFrameworkCore;

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
        TimeProvider? timeProvider = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options, currentUser, currentTenant, dataFilterManager, timeProvider, multiTenancyOptions)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        if (!optionsBuilder.IsConfigured)
            return;

        // Register pgvector type mappings so EF Core treats Vector as a scalar type.
        // We read the existing connection string from the NpgsqlOptionsExtension via reflection
        // to avoid overriding it with null when calling UseNpgsql again.
        var npgsqlExtension = optionsBuilder.Options.Extensions
            .FirstOrDefault(ext => ext.GetType().Name == "NpgsqlOptionsExtension");

        if (npgsqlExtension == null)
            return;

        var connString = npgsqlExtension.GetType()
            .GetProperty("ConnectionString")
            ?.GetValue(npgsqlExtension) as string;

        if (string.IsNullOrEmpty(connString))
            return;

        optionsBuilder.UseNpgsql(connString, o => o.UseVector());
    }
}
