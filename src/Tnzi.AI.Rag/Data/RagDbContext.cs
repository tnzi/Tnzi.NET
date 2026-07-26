using Microsoft.EntityFrameworkCore.Infrastructure;
using Pgvector.EntityFrameworkCore;

namespace Tnzi.AI.Rag.Data;

/// <summary>
/// RAG 模块独立 DbContext - 使用 PostgreSQL + pgvector
/// </summary>
/// <remarks>
/// 实体通过 EntityTypeConfigurationBase.DbContextType 自动绑定到此 DbContext，
/// 由 TnziDbContextHelper.OnModelCreating 统一注册，无需手动声明 DbSet。
/// 应用层需在 appsettings.json 的 Database:DbContexts 中配置此 DbContext。
/// </remarks>
public class RagDbContext : TnziDbContext<RagDbContext>
{
    /// <summary>
    /// 设计时迁移程序集名称回退。
    /// <para>
    /// RagDbContext 定义在框架库，但迁移文件由消费应用持有。运行时优先读取
    /// <see cref="AIRagOptions.MigrationsAssembly"/>；设计时（<c>dotnet ef</c>）DI 不可用，
    /// 由消费应用的 <c>IDesignTimeDbContextFactory&lt;RagDbContext&gt;</c> 在创建上下文前设置此字段。
    /// 对齐框架 <c>TableNamePrefixConfiguration.DesignTimeModuleContainer</c> 的设计时注入惯例。
    /// </para>
    /// </summary>
    public static string? DesignTimeMigrationsAssembly { get; set; }

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

        var migrationsAssembly = ResolveMigrationsAssembly(optionsBuilder);

        optionsBuilder.UseNpgsql(connString, o =>
        {
            o.UseVector();

            // RagDbContext 是独立 DbContext。即便与主业务共用同一物理数据库，也必须使用
            // 独立的迁移历史表（rag schema 下的 __EFMigrationsHistory），否则会与主 DbContext
            // 共享 public.__EFMigrationsHistory，导致迁移历史互相污染、RAG 表被误判为"已是最新"而永不创建。
            o.MigrationsHistoryTable("__EFMigrationsHistory", "rag");

            // 迁移文件由消费应用持有（嵌入维度可配置，不能锁死在框架库迁移里）。
            if (!string.IsNullOrEmpty(migrationsAssembly))
                o.MigrationsAssembly(migrationsAssembly);
        });
    }

    /// <summary>
    /// 解析 RagDbContext 迁移文件所在程序集：运行时优先读 <see cref="AIRagOptions.MigrationsAssembly"/>，
    /// 设计时回退到 <see cref="DesignTimeMigrationsAssembly"/>。两者均为空时返回 null（EF 回退到本程序集）。
    /// </summary>
    private static string? ResolveMigrationsAssembly(DbContextOptionsBuilder optionsBuilder)
    {
        var serviceProvider = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
        var fromOptions = serviceProvider?.GetService<IOptions<AIRagOptions>>()?.Value.MigrationsAssembly;
        return !string.IsNullOrEmpty(fromOptions) ? fromOptions : DesignTimeMigrationsAssembly;
    }
}
