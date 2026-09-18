using Microsoft.Extensions.Options;
using MockQueryable;
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

    private static IServiceProvider BuildSp(
        List<User> users,
        Guid currentUserId,
        List<UserDetail>? details = null,
        IReadOnlySet<Guid>? superAdmins = null,
        IReadOnlySet<Guid>? disabledUsers = null,
        ChatOptions? options = null,
        IChatAccessService? access = null)
    {
        var services = new ServiceCollection();

        var optionsMock = new Mock<IOptionsSnapshot<ChatOptions>>();
        optionsMock.SetupGet(o => o.Value).Returns(options ?? new ChatOptions());
        services.AddSingleton(optionsMock.Object);

        var userRepo = new Mock<IRepository<User, Guid>>();
        userRepo.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<User, bool>> p, CancellationToken _) =>
                users.Where(p.Compile()).ToList());
        // 目录搜索走 IQueryable 分页（SQL 侧 ORDER BY + TAKE），这里给一个支持 EF 异步的内存查询。
        userRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(users.BuildMock());
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
        if (access != null)
        {
            services.AddSingleton(access);
        }
        else if (disabledUsers != null)
        {
            var accessMock = new Mock<IChatAccessService>();
            accessMock.Setup(a => a.FilterDisabledAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync((IEnumerable<Guid> ids) =>
                    (IReadOnlySet<Guid>)ids.Where(disabledUsers.Contains).ToHashSet());
            services.AddSingleton(accessMock.Object);
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

    /// <summary>
    /// ★ <c>GET chat/contacts/{userId}/profile</c> 对任意 id 回邮箱与电话是 06-21 的开放目录设计
    /// （持 chat.use 即可查全目录）。不是每个部署都接受这一点：
    /// <c>ChatOptions.ExposeContactDetails = false</c> 时投影里不含 Email / Phone（Bio 是本人
    /// 自己写的简介，照旧）。默认 true 保持兼容。
    /// </summary>
    [Fact]
    public async Task GetProfile_OmitsEmailAndPhone_WhenContactDetailsAreNotExposed()
    {
        var me = Guid.NewGuid();
        var target = new User { Id = Guid.NewGuid(), UserName = "carol", Email = "carol@example.com", PhoneNumber = "+1-555-0100" };
        var detail = new UserDetail { UserId = target.Id, Nickname = "Carol", Bio = "Hello there" };
        var sp = BuildSp(
            new List<User> { new() { Id = me, UserName = "alpha" }, target }, me,
            new List<UserDetail> { detail },
            options: new ChatOptions { ExposeContactDetails = false });
        var svc = BuildService(sp);

        var result = await svc.GetProfileAsync(target.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Name.ShouldBe("Carol");
        result.Data!.Email.ShouldBeNull();
        result.Data!.Phone.ShouldBeNull();
        result.Data!.Bio.ShouldBe("Hello there");
    }

    /// <summary>
    /// 记录每次 <see cref="IChatAccessService.FilterDisabledAsync"/> 收到多少个 id 的门。
    /// 通讯录搜索的代价就在这里：交给它的 id 数就是被物化并逐个判权的用户数。
    /// </summary>
    private sealed class CountingAccess(IReadOnlySet<Guid> disabled) : IChatAccessService
    {
        public List<int> BatchSizes { get; } = [];
        public Task<bool> CanUseAsync(Guid userId) => Task.FromResult(!disabled.Contains(userId));
        public Task<bool> CanCurrentUserUseAsync() => Task.FromResult(true);
        public Task<IReadOnlySet<Guid>> FilterDisabledAsync(IEnumerable<Guid> userIds)
        {
            var ids = userIds.ToList();
            BatchSizes.Add(ids.Count);
            return Task.FromResult<IReadOnlySet<Guid>>(ids.Where(disabled.Contains).ToHashSet());
        }
    }

    private static List<User> Directory(int count) =>
        Enumerable.Range(0, count).Select(i => new User { Id = Guid.NewGuid(), UserName = $"user{i:D4}" }).ToList();

    /// <summary>
    /// ★ 空关键字此前把整张 User 表读进内存、对每个用户各跑一次权限查询，最后才 Take(20)：
    /// 10 万用户的部署点一次「新建对话」= 10 万个实体 + 十万级权限往返。分页必须在 SQL 侧，
    /// 权限判定只对那一小窗候选做。
    /// </summary>
    [Fact]
    public async Task SearchUsers_BlankKeyword_DoesNotMaterialiseWholeDirectory()
    {
        var me = Guid.NewGuid();
        var users = Directory(200);
        users.Add(new User { Id = me, UserName = "me" });
        var access = new CountingAccess(new HashSet<Guid>());
        var svc = BuildService(BuildSp(users, me, access: access));

        var result = await svc.SearchUsersAsync("");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(20);
        access.BatchSizes.ShouldHaveSingleItem();
        access.BatchSizes[0].ShouldBeLessThanOrEqualTo(20 * ChatContactService.CandidateWindowFactor);
    }

    [Fact]
    public async Task SearchUsers_Keyword_QueryIsBounded()
    {
        var me = Guid.NewGuid();
        var users = Directory(200);
        users.Add(new User { Id = me, UserName = "me" });
        var access = new CountingAccess(new HashSet<Guid>());
        var svc = BuildService(BuildSp(users, me, access: access));

        // 关键字命中全部 200 个用户，交给权限判定的仍只能是一小窗。
        var result = await svc.SearchUsersAsync("user");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(20);
        access.BatchSizes.ShouldHaveSingleItem();
        access.BatchSizes[0].ShouldBeLessThanOrEqualTo(20 * ChatContactService.CandidateWindowFactor);
    }

    /// <summary>
    /// 候选窗比页大，所以窗内少数没有 chat.use 的用户被剔掉后页面仍然填满。
    /// </summary>
    [Fact]
    public async Task SearchUsers_PageStillFills_WhenSomeCandidatesLackChatUse()
    {
        var me = Guid.NewGuid();
        var users = Directory(200);
        users.Add(new User { Id = me, UserName = "me" });
        var disabled = users.Take(10).Select(u => u.Id).ToHashSet();
        var access = new CountingAccess(disabled);
        var svc = BuildService(BuildSp(users, me, access: access));

        var result = await svc.SearchUsersAsync("");

        result.Data!.Count.ShouldBe(20);
        result.Data!.Select(c => c.UserId).ShouldNotContain(id => disabled.Contains(id));
    }

    [Fact]
    public async Task SearchUsers_ResultsAreOrderedByUserName()
    {
        var me = Guid.NewGuid();
        var users = Directory(50);
        users.Reverse();
        users.Add(new User { Id = me, UserName = "me" });
        var svc = BuildService(BuildSp(users, me));

        var result = await svc.SearchUsersAsync("");

        result.Data!.Select(c => c.Name).ShouldBe(result.Data!.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
    }
}
