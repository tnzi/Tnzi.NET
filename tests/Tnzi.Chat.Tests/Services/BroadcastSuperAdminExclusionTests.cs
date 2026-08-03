using System.Linq.Expressions;
using Tnzi.Identity.Entities;
using Tnzi.Security.Authorization;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// The "broadcast to all users" path must skip super admins: they are
/// maintenance/operations accounts, not business recipients. Explicit role/user
/// targeting is intentional and stays unfiltered (covered elsewhere).
/// </summary>
public class BroadcastSuperAdminExclusionTests : Integration.IntegrationTestBase
{
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();
    private static readonly Guid SuperAdmin = Guid.NewGuid();

    private IBroadcastService Broadcast => ServiceProvider.GetRequiredService<IBroadcastService>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Replace the base's empty user-repo mock. This one honours the predicate so the
        // service's "exclude super admins" filter is actually exercised.
        var allUsers = new List<User>
        {
            new() { Id = UserA, UserName = "alice" },
            new() { Id = UserB, UserName = "bob" },
            new() { Id = SuperAdmin, UserName = "root" },
        };
        var userRepo = new Mock<IRepository<User, Guid>>();
        userRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<User, bool>> p, CancellationToken _) => allUsers.Where(p.Compile()).ToList());
        services.AddScoped(_ => userRepo.Object);

        var funcAuth = new Mock<IFunctionAuthorizationService>();
        funcAuth.Setup(f => f.GetSuperAdminUserIdsAsync())
            .ReturnsAsync((IReadOnlySet<Guid>)new HashSet<Guid> { SuperAdmin });
        services.AddScoped(_ => funcAuth.Object);
    }

    [Fact]
    public async Task BroadcastAll_Should_Skip_SuperAdmins()
    {
        var r = await Broadcast.BroadcastAsync(new BroadcastDto { Content = "System-wide maintenance tonight", All = true });

        r.Succeeded.ShouldBeTrue(r.Message);
        r.Data.ShouldBe(2); // only the two business users; the super admin is skipped

        // The super admin gets NO system-notification conversation.
        var superKey = $"system:{SuperAdmin:N}";
        (await DbContext.Set<Conversation>().AnyAsync(c => c.DirectKey == superKey)).ShouldBeFalse();

        // Business users do.
        foreach (var uid in new[] { UserA, UserB })
        {
            var key = $"system:{uid:N}";
            (await DbContext.Set<Conversation>().AnyAsync(c => c.DirectKey == key)).ShouldBeTrue();
        }
    }
}
