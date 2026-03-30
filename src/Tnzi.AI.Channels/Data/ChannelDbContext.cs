using Tnzi.Security.Claims;

namespace Tnzi.AI.Channels.Data;

/// <summary>
/// Channels 模块 DbContext
/// </summary>
/// <remarks>
/// 实体通过 EntityTypeConfigurationBase.DbContextType 自动绑定，
/// 由 TnziDbContextHelper.OnModelCreating 统一注册，无需手动声明 DbSet。
/// </remarks>
public class ChannelDbContext : TnziDbContext<ChannelDbContext>
{
    public ChannelDbContext(
        DbContextOptions<ChannelDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null,
        TimeProvider? timeProvider = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options, currentUser, currentTenant, dataFilterManager, timeProvider, multiTenancyOptions)
    {
    }
}
