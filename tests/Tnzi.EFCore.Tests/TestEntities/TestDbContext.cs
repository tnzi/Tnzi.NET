
namespace Tnzi.EFCore.Tests.TestEntities;

/// <summary>
/// 测试用 DbContext
/// </summary>
public class TestDbContext : TnziDbContext<TestDbContext>
{
    public TestDbContext(
        DbContextOptions<TestDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null)
        : base(options, currentUser, currentTenant, dataFilterManager)
    {
    }

    /// <summary>
    /// 用户表
    /// </summary>
    public DbSet<TestUser> Users { get; set; } = null!;

    /// <summary>
    /// 产品表
    /// </summary>
    public DbSet<TestProduct> Products { get; set; } = null!;

    /// <summary>
    /// Guid Id 测试实体
    /// </summary>
    public DbSet<TestEntityWithGuidId> TestEntitiesWithGuidId { get; set; } = null!;

    /// <summary>
    /// 可软删除产品表
    /// </summary>
    public DbSet<TestSoftDeletableProduct> SoftDeletableProducts { get; set; } = null!;

    /// <summary>
    /// 可空 Guid Id 测试实体
    /// </summary>
    public DbSet<TestEntityWithNullableGuidId> TestEntitiesWithNullableGuidId { get; set; } = null!;

    /// <summary>
    /// long Id 测试实体
    /// </summary>
    public DbSet<TestEntityWithLongId> TestEntitiesWithLongId { get; set; } = null!;

    /// <summary>
    /// int Id 测试实体
    /// </summary>
    public DbSet<TestEntityWithIntId> TestEntitiesWithIntId { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置实体
        modelBuilder.Entity<TestUser>(entity =>
        {
            entity.ToTable("TestUsers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<TestProduct>(entity =>
        {
            entity.ToTable("TestProducts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Price).HasPrecision(18, 2);
        });

        // 配置测试实体
        modelBuilder.Entity<TestEntityWithGuidId>(entity =>
        {
            entity.ToTable("TestEntitiesWithGuidId");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<TestEntityWithNullableGuidId>(entity =>
        {
            entity.ToTable("TestEntitiesWithNullableGuidId");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<TestEntityWithLongId>(entity =>
        {
            entity.ToTable("TestEntitiesWithLongId");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<TestEntityWithIntId>(entity =>
        {
            entity.ToTable("TestEntitiesWithIntId");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<TestSoftDeletableProduct>(entity =>
        {
            entity.ToTable("TestSoftDeletableProducts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Price).HasPrecision(18, 2);
        });

        // 确保查询过滤器在所有实体配置之后应用
        ConfigureQueryFilters(modelBuilder);
    }
}