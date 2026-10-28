using System.Linq.Expressions;
using Tnzi.Chat.Tests.Integration;
using Tnzi.Identity.Entities;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 建群、加人、广播只接受存在的账号：单租户、未加载 Authorization 时也一样。
/// </summary>
/// <remarks>
/// 这两个条件下另外两道筛子都放行任意 id：租户边界不裁剪（单租户原样返回、不查库），
/// <c>chat.use</c> 门未激活（没有权限检查器时一律可用）。此前一个不存在的 id 会得到一条成员行并计入
/// <c>MemberCount</c>，或者一个永远没人看的系统会话还被计作送达。处理口径沿用各自的既有规则：
/// 建群静默剔除，加人剔除后没有可加的人答 400，广播计为投递失败。
/// </remarks>
public class ChatUnknownUserTests : IntegrationTestBase
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Ghost = Guid.NewGuid();

    private static readonly List<User> Users =
    [
        new() { Id = CurrentUserId, UserName = "me" },
        new() { Id = Alice, UserName = "alice" },
        new() { Id = Bob, UserName = "bob" },
    ];

    private IGroupService Groups => ServiceProvider.GetRequiredService<IGroupService>();
    private IBroadcastService Broadcasts => ServiceProvider.GetRequiredService<IBroadcastService>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var userRepo = new Mock<IRepository<User, Guid>>();
        userRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<User, bool>> p, CancellationToken _) => Users.Where(p.Compile()).ToList());
        userRepo.Setup(r => r.GetListAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) => Users.Where(u => ids.Contains(u.Id)).ToList());
        userRepo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Users.Any(u => u.Id == id));
        services.AddScoped(_ => userRepo.Object);
    }

    [Fact]
    public async Task CreateGroup_drops_an_unknown_id_and_counts_only_real_members()
    {
        var result = await Groups.CreateGroupAsync(new CreateGroupDto { Title = "Team", MemberIds = [Alice, Ghost] });

        result.Succeeded.ShouldBeTrue(result.Message);
        var conversation = await DbContext.Set<Conversation>().AsNoTracking().SingleAsync(c => c.Id == result.Data!.Id);
        conversation.MemberCount.ShouldBe(2);

        var memberIds = await MemberUserIdsAsync(result.Data!.Id);
        memberIds.ShouldBe([CurrentUserId, Alice], ignoreOrder: true);
    }

    [Fact]
    public async Task AddMembers_with_only_unknown_ids_is_refused_and_adds_nothing()
    {
        var group = (await Groups.CreateGroupAsync(new CreateGroupDto { Title = "Team", MemberIds = [] })).Data!;

        var result = await Groups.AddMembersAsync(group.Id, [Ghost]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await MemberUserIdsAsync(group.Id)).ShouldBe([CurrentUserId]);
    }

    [Fact]
    public async Task AddMembers_adds_the_real_accounts_and_skips_the_unknown_one()
    {
        var group = (await Groups.CreateGroupAsync(new CreateGroupDto { Title = "Team", MemberIds = [] })).Data!;

        var result = await Groups.AddMembersAsync(group.Id, [Bob, Ghost]);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await MemberUserIdsAsync(group.Id)).ShouldBe([CurrentUserId, Bob], ignoreOrder: true);
        var conversation = await DbContext.Set<Conversation>().AsNoTracking().SingleAsync(c => c.Id == group.Id);
        conversation.MemberCount.ShouldBe(2);
    }

    [Fact]
    public async Task Broadcast_to_an_unknown_id_is_reported_as_not_delivered_and_creates_nothing_for_it()
    {
        var result = await Broadcasts.BroadcastToUsersAsync([Alice, Ghost], "Maintenance at 21:00 UTC");

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(1);
        result.Message!.ShouldContain("could not be delivered");

        var ghostKey = $"system:{Ghost:N}";
        (await DbContext.Set<Conversation>().IgnoreQueryFilters().AsNoTracking().AnyAsync(c => c.DirectKey == ghostKey)).ShouldBeFalse();
        (await DbContext.Set<ConversationMember>().AsNoTracking().AnyAsync(m => m.UserId == Ghost)).ShouldBeFalse();

        var aliceKey = $"system:{Alice:N}";
        (await DbContext.Set<Conversation>().AsNoTracking().AnyAsync(c => c.DirectKey == aliceKey)).ShouldBeTrue();
    }

    private Task<List<Guid>> MemberUserIdsAsync(Guid conversationId)
        => DbContext.Set<ConversationMember>().AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.RemovedAt == null)
            .Select(m => m.UserId)
            .ToListAsync();
}
