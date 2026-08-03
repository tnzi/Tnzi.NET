using Microsoft.Extensions.Options;
using Tnzi.Chat.Options;
using Tnzi.Identity.Entities;
using Tnzi.Security.Authorization;

namespace Tnzi.Chat.Tests.Services;

public class ChatContactServiceTests
{
    private static ChatContactService BuildService(IServiceProvider sp)
        => new(sp,
            sp.GetRequiredService<IRepository<User, Guid>>(),
            sp.GetRequiredService<IRepository<UserDetail, Guid>>(),
            sp.GetRequiredService<IPresenceService>(),
            sp.GetRequiredService<IOptionsSnapshot<ChatOptions>>(),
            // Optional - null unless a test registers a super-admin source.
            sp.GetService<IFunctionAuthorizationService>(),
            // Optional - null unless a test registers a chat.use gate.
            sp.GetService<IChatAccessService>());

    private static IServiceProvider BuildSp(List<User> users, Guid currentUserId, List<UserDetail>? details = null, IReadOnlySet<Guid>? superAdmins = null, IReadOnlySet<Guid>? disabledUsers = null)
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

        // Only present when a test wants super admins hidden. Absent → ChatContactService's
        // optional dependency resolves to null → no one is hidden.
        if (superAdmins != null)
        {
            var funcAuth = new Mock<IFunctionAuthorizationService>();
            funcAuth.Setup(f => f.GetSuperAdminUserIdsAsync()).ReturnsAsync(superAdmins);
            services.AddSingleton(funcAuth.Object);
        }

        // Only present when a test wants the chat.use gate active. Absent → the optional
        // IChatAccessService resolves to null → fail-open (nobody hidden). The mock reports
        // exactly the input ids that intersect `disabledUsers` as lacking chat.use.
        if (disabledUsers != null)
        {
            var access = new Mock<IChatAccessService>();
            access.Setup(a => a.FilterDisabledAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync((IEnumerable<Guid> ids) =>
                    (IReadOnlySet<Guid>)ids.Where(disabledUsers.Contains).ToHashSet());
            services.AddSingleton(access.Object);
        }

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
    public async Task SearchUsers_BlankKeyword_Should_Exclude_SuperAdmins()
    {
        var me = Guid.NewGuid();
        var alice = new User { Id = Guid.NewGuid(), UserName = "alice" };
        var root = new User { Id = Guid.NewGuid(), UserName = "root" }; // a super admin
        var meUser = new User { Id = me, UserName = "alpha" };
        var sp = BuildSp(new List<User> { alice, root, meUser }, me, superAdmins: new HashSet<Guid> { root.Id });
        var svc = BuildService(sp);

        // The business-facing directory must never surface a super-admin account.
        var result = await svc.SearchUsersAsync("");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(c => c.UserId).ShouldContain(alice.Id);
        result.Data!.Select(c => c.UserId).ShouldNotContain(root.Id);
        result.Data!.Select(c => c.UserId).ShouldNotContain(me);
    }

    [Fact]
    public async Task SearchUsers_WithKeyword_Should_Exclude_SuperAdmins()
    {
        var me = Guid.NewGuid();
        var root = new User { Id = Guid.NewGuid(), UserName = "root" }; // super admin, name matches keyword
        var meUser = new User { Id = me, UserName = "alpha" };
        var sp = BuildSp(new List<User> { root, meUser }, me, superAdmins: new HashSet<Guid> { root.Id });
        var svc = BuildService(sp);

        // Even an exact keyword match on a super admin returns nothing.
        var result = await svc.SearchUsersAsync("root");

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchUsers_Should_Exclude_Users_Without_ChatUse()
    {
        var me = Guid.NewGuid();
        var alice = new User { Id = Guid.NewGuid(), UserName = "alice" };
        var bob = new User { Id = Guid.NewGuid(), UserName = "bob" }; // lacks chat.use
        var meUser = new User { Id = me, UserName = "alpha" };
        var sp = BuildSp(new List<User> { alice, bob, meUser }, me, disabledUsers: new HashSet<Guid> { bob.Id });
        var svc = BuildService(sp);

        // Users without chat.use can't participate - they must not appear in the
        // new-chat / add-member picker.
        var result = await svc.SearchUsersAsync("");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(c => c.UserId).ShouldContain(alice.Id);
        result.Data!.Select(c => c.UserId).ShouldNotContain(bob.Id);
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
