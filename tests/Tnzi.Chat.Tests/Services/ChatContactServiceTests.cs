using Microsoft.Extensions.Options;
using Moq;
using Tnzi.Chat.Options;
using Tnzi.Chat.Services;
using Tnzi.Domain.Repositories;
using Tnzi.Identity.Entities;

namespace Tnzi.Chat.Tests.Services;

public class ChatContactServiceTests
{
    private static ChatContactService BuildService(IServiceProvider sp)
        => new(sp,
            sp.GetRequiredService<IRepository<User, Guid>>(),
            sp.GetRequiredService<IRepository<UserDetail, Guid>>(),
            sp.GetRequiredService<IPresenceService>(),
            sp.GetRequiredService<IOptionsSnapshot<ChatOptions>>());

    private static IServiceProvider BuildSp(List<User> users, Guid currentUserId, List<UserDetail>? details = null)
    {
        var services = new ServiceCollection();

        var optionsMock = new Mock<IOptionsSnapshot<ChatOptions>>();
        optionsMock.SetupGet(o => o.Value).Returns(new ChatOptions());
        services.AddSingleton(optionsMock.Object);

        var userRepo = new Mock<IRepository<User, Guid>>();
        userRepo.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<User, bool>> p, CancellationToken _) =>
                users.Where(p.Compile()).ToList());
        userRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<User, bool>> p, CancellationToken _) =>
                users.FirstOrDefault(p.Compile()));
        services.AddSingleton(userRepo.Object);

        // UserDetail rows (if any) supply Nickname/Avatar/Bio; otherwise display
        // name falls back to UserName, avatar/bio are null.
        var detailRows = details ?? new List<UserDetail>();
        var userDetailRepo = new Mock<IRepository<UserDetail, Guid>>();
        userDetailRepo.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserDetail, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<UserDetail, bool>> p, CancellationToken _) =>
                detailRows.Where(p.Compile()).ToList());
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
        var svc = BuildService(sp);

        var result = await svc.SearchUsersAsync("al");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(c => c.UserId).ShouldContain(alice.Id);
        result.Data!.Select(c => c.UserId).ShouldNotContain(me);
    }

    [Fact]
    public async Task SearchUsers_BlankKeyword_Should_Return_Directory_Excluding_Self()
    {
        var me = Guid.NewGuid();
        var alice = new User { Id = Guid.NewGuid(), UserName = "alice" };
        var bob = new User { Id = Guid.NewGuid(), UserName = "bob" };
        var meUser = new User { Id = me, UserName = "alpha" };
        var sp = BuildSp(new List<User> { alice, bob, meUser }, me);
        var svc = BuildService(sp);

        // Blank keyword returns the first page of the directory (so the new-chat picker
        // can show a starting list) but still excludes the current user.
        var result = await svc.SearchUsersAsync("   ");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(c => c.UserId).ShouldContain(alice.Id);
        result.Data!.Select(c => c.UserId).ShouldContain(bob.Id);
        result.Data!.Select(c => c.UserId).ShouldNotContain(me);
    }

    [Fact]
    public async Task GetProfile_NonExistentUser_Should_Return_404()
    {
        var me = Guid.NewGuid();
        // Directory has only "me"; the looked-up id is not present.
        var sp = BuildSp(new List<User> { new() { Id = me, UserName = "alpha" } }, me);
        var svc = BuildService(sp);

        var result = await svc.GetProfileAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task GetProfile_Should_Include_Email_Phone_Bio_When_Available()
    {
        var me = Guid.NewGuid();
        var target = new User { Id = Guid.NewGuid(), UserName = "carol", Email = "carol@example.com", PhoneNumber = "+1-555-0100" };
        var detail = new UserDetail { UserId = target.Id, Nickname = "Carol", Bio = "Hello there" };
        var sp = BuildSp(new List<User> { new() { Id = me, UserName = "alpha" }, target }, me, new List<UserDetail> { detail });
        var svc = BuildService(sp);

        var result = await svc.GetProfileAsync(target.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Name.ShouldBe("Carol");
        result.Data!.Email.ShouldBe("carol@example.com");
        result.Data!.Phone.ShouldBe("+1-555-0100");
        result.Data!.Bio.ShouldBe("Hello there");
    }
}
