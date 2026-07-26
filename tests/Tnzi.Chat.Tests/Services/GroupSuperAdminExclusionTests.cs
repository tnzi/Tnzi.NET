using Moq;
using Tnzi.Security.Authorization;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// Group-member write paths take caller-supplied ids, so they must enforce the same
/// "no super admins in a business group" rule the contact directory relies on -
/// otherwise a maintenance account could be pulled into a group by passing its id
/// directly, bypassing the (already-filtered) picker. Dropping is silent (not 403):
/// one stray id shouldn't fail the whole operation, nor disclose that it's a super admin.
/// </summary>
public class GroupSuperAdminExclusionTests : Integration.IntegrationTestBase
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly Guid SuperAdmin = Guid.NewGuid();

    private IGroupService Groups => ServiceProvider.GetRequiredService<IGroupService>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var funcAuth = new Mock<IFunctionAuthorizationService>();
        funcAuth.Setup(f => f.GetSuperAdminUserIdsAsync())
            .ReturnsAsync((IReadOnlySet<Guid>)new HashSet<Guid> { SuperAdmin });
        services.AddScoped(_ => funcAuth.Object);
    }

    [Fact]
    public async Task CreateGroup_Should_Drop_SuperAdmin_Members()
    {
        var result = await Groups.CreateGroupAsync(new CreateGroupDto
        {
            Title = "Team",
            MemberIds = new List<Guid> { Business, SuperAdmin },
        });

        result.Succeeded.ShouldBeTrue(result.Message);

        var conv = await DbContext.Set<Conversation>().FirstAsync(c => c.Type == ConversationType.Group);
        var memberIds = await DbContext.Set<ConversationMember>()
            .Where(m => m.ConversationId == conv.Id)
            .Select(m => m.UserId)
            .ToListAsync();

        memberIds.ShouldContain(Business);
        memberIds.ShouldContain(CurrentUserId); // the owner
        memberIds.ShouldNotContain(SuperAdmin);
    }

    [Fact]
    public async Task AddMembers_Should_Drop_SuperAdmin()
    {
        var created = await Groups.CreateGroupAsync(new CreateGroupDto { Title = "Ops", MemberIds = new List<Guid>() });
        created.Succeeded.ShouldBeTrue(created.Message);
        var convId = created.Data!.Id;

        var add = await Groups.AddMembersAsync(convId, new[] { Business, SuperAdmin });
        add.Succeeded.ShouldBeTrue(add.Message);

        var memberIds = await DbContext.Set<ConversationMember>()
            .Where(m => m.ConversationId == convId && m.RemovedAt == null)
            .Select(m => m.UserId)
            .ToListAsync();

        memberIds.ShouldContain(Business);
        memberIds.ShouldNotContain(SuperAdmin);
    }

    [Fact]
    public async Task AddMembers_AllSuperAdmins_Should_Fail_NoMembers()
    {
        var created = await Groups.CreateGroupAsync(new CreateGroupDto { Title = "Solo", MemberIds = new List<Guid>() });
        created.Succeeded.ShouldBeTrue(created.Message);

        // Adding only a super admin resolves to an empty set after exclusion.
        var add = await Groups.AddMembersAsync(created.Data!.Id, new[] { SuperAdmin });

        add.Succeeded.ShouldBeFalse();
        add.Code.ShouldBe(400);
    }
}
