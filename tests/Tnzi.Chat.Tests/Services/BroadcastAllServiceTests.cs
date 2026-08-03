using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using Tnzi.Identity.Entities;
using Tnzi.MultiTenancy;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// Covers the "broadcast to all users" path (BroadcastDto.All), which enumerates
/// every user via the user repository and delivers a System notification to each.
/// A dedicated test class is needed because it re-registers the user-repo mock
/// (the base registers an empty one).
/// </summary>
public class BroadcastAllServiceTests : Integration.IntegrationTestBase
{
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    private IBroadcastService Broadcast => ServiceProvider.GetRequiredService<IBroadcastService>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Replace the base's empty user-repo mock so the "all" path enumerates two users.
        var userRepo = new Mock<IRepository<User, Guid>>();
        userRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>
            {
                new() { Id = UserA, UserName = "alice" },
                new() { Id = UserB, UserName = "bob" },
            });
        services.AddScoped(_ => userRepo.Object);
    }

    [Fact]
    public async Task BroadcastAll_Should_Notify_Every_User()
    {
        var r = await Broadcast.BroadcastAsync(new BroadcastDto { Content = "System-wide maintenance tonight", All = true });

        r.Succeeded.ShouldBeTrue(r.Message);
        r.Data.ShouldBe(2);

        foreach (var uid in new[] { UserA, UserB })
        {
            var key = $"system:{uid:N}";
            var conv = await DbContext.Set<Conversation>().FirstAsync(c => c.DirectKey == key);
            conv.Type.ShouldBe(ConversationType.System);

            var member = await DbContext.Set<ConversationMember>()
                .FirstAsync(m => m.ConversationId == conv.Id && m.UserId == uid);
            member.UnreadCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Broadcast_WithNoTargets_AndNotAll_Should_Fail()
    {
        var r = await Broadcast.BroadcastAsync(new BroadcastDto { Content = "orphan" });

        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(400);
    }
}

/// <summary>
/// In multi-tenant mode the "broadcast to all" path must be rejected: User is not
/// IMultiTenant, so enumerating every user would cross tenant boundaries.
/// </summary>
public class BroadcastMultiTenantGuardTests : Integration.IntegrationTestBase
{
    private IBroadcastService Broadcast => ServiceProvider.GetRequiredService<IBroadcastService>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // Override the base's MT-off registration → enable multi-tenancy.
        services.AddSingleton<IOptions<MultiTenancyOptions>>(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));
    }

    [Fact]
    public async Task BroadcastAll_InMultiTenantMode_Should_Fail_400()
    {
        var r = await Broadcast.BroadcastAsync(new BroadcastDto { Content = "System-wide notice", All = true });

        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(400);
    }
}
