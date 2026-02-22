
namespace Tnzi.EFCore.Tests.TestEntities;

/// <summary>
/// 用于测试自动发现的默认 DbContext
/// </summary>
public class DefaultDbContext : TnziDbContext<DefaultDbContext>
{
    public DefaultDbContext(
        DbContextOptions<DefaultDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null)
        : base(options, currentUser, currentTenant, dataFilterManager)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}