using Moq;
using Tnzi.Chat.Services;
using Tnzi.Domain.Repositories;
using Tnzi.Identity.Entities;

namespace Tnzi.Chat.Tests.Services;

public class ChatContactServiceTests
{
    private static IServiceProvider BuildSp(List<User> users, Guid currentUserId)
    {
        var services = new ServiceCollection();

        var userRepo = new Mock<IRepository<User, Guid>>();
        userRepo.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<User, bool>> p, CancellationToken _) =>
                users.Where(p.Compile()).ToList());
        services.AddSingleton(userRepo.Object);

        // No UserDetail rows → display name falls back to UserName, avatar is null.
        var userDetailRepo = new Mock<IRepository<UserDetail, Guid>>();
        userDetailRepo.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserDetail, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDetail>());
        services.AddSingleton(userDetailRepo.Object);

        var presenceMock = new Mock<IPresenceService>();
        presenceMock.Setup(p => p.ResolveEffectiveAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(Array.Empty<UserPresenceDto>());
        services.AddSingleton(presenceMock.Object);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.Id).Returns((Guid?)currentUserId);
        services.AddSingleton(currentUser.Object);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SearchUsers_Should_Exclude_Self_And_Match_Keyword()
    {
        var me = Guid.NewGuid();
        var alice = new User { Id = Guid.NewGuid(), UserName = "alice" };
        var bob = new User { Id = Guid.NewGuid(), UserName = "bob" };
        var meUser = new User { Id = me, UserName = "alpha" };
        var sp = BuildSp(new List<User> { alice, bob, meUser }, me);
        var svc = new ChatContactService(sp, sp.GetRequiredService<IRepository<User, Guid>>(), sp.GetRequiredService<IRepository<UserDetail, Guid>>(), sp.GetRequiredService<IPresenceService>());

        var result = await svc.SearchUsersAsync("al");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(c => c.UserId).ShouldContain(alice.Id);
        result.Data!.Select(c => c.UserId).ShouldNotContain(me);
    }

    [Fact]
    public async Task GetProfile_NonExistentUser_Should_Return_404()
    {
        var me = Guid.NewGuid();
        // Directory has only "me"; the looked-up id is not present.
        var sp = BuildSp(new List<User> { new() { Id = me, UserName = "alpha" } }, me);
        var svc = new ChatContactService(sp, sp.GetRequiredService<IRepository<User, Guid>>(), sp.GetRequiredService<IRepository<UserDetail, Guid>>(), sp.GetRequiredService<IPresenceService>());

        var result = await svc.GetProfileAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }
}
