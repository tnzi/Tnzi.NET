namespace Tnzi.Chat.Tests.Services;

public class GroupServiceTests : Integration.IntegrationTestBase
{
    private IGroupService Group => ServiceProvider.GetRequiredService<IGroupService>();

    [Fact]
    public async Task CreateGroup_Should_Set_Owner_And_Members()
    {
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        var r = await Group.CreateGroupAsync(new CreateGroupDto { Title = "Team", MemberIds = new List<Guid> { m1, m2 } });

        r.Succeeded.ShouldBeTrue(r.Message);
        r.Data!.OwnerId.ShouldBe(CurrentUserId);
        r.Data.Type.ShouldBe(ConversationType.Group);
        // 成员 = 创建者 + m1 + m2
        (await DbContext.Set<ConversationMember>().CountAsync(m => m.ConversationId == r.Data.Id && m.RemovedAt == null)).ShouldBe(3);
        var owner = await DbContext.Set<ConversationMember>().FirstAsync(m => m.ConversationId == r.Data.Id && m.UserId == CurrentUserId);
        owner.Role.ShouldBe(MemberRole.Owner);
    }

    [Fact]
    public async Task Rename_By_NonOwner_Should_Fail_403()
    {
        // 建群后，把当前用户从 Owner 降级模拟（直接改库造非 owner 场景）
        var g = (await Group.CreateGroupAsync(new CreateGroupDto { Title = "G", MemberIds = new List<Guid>() })).Data!;
        var owner = await DbContext.Set<ConversationMember>().FirstAsync(m => m.ConversationId == g.Id && m.UserId == CurrentUserId);
        owner.Role = MemberRole.Member;
        var conv = await DbContext.Set<Conversation>().FindAsync(g.Id);
        conv!.OwnerId = Guid.NewGuid();
        await DbContext.SaveChangesAsync();

        var r = await Group.RenameGroupAsync(g.Id, "New");
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);
    }

    [Fact]
    public async Task AddRemove_Members_And_Leave()
    {
        var g = (await Group.CreateGroupAsync(new CreateGroupDto { Title = "G", MemberIds = new List<Guid>() })).Data!;
        var newGuy = Guid.NewGuid();

        (await Group.AddMembersAsync(g.Id, new[] { newGuy })).Succeeded.ShouldBeTrue();
        (await DbContext.Set<ConversationMember>().CountAsync(m => m.ConversationId == g.Id && m.RemovedAt == null)).ShouldBe(2);

        (await Group.RemoveMemberAsync(g.Id, newGuy)).Succeeded.ShouldBeTrue();
        (await DbContext.Set<ConversationMember>().CountAsync(m => m.ConversationId == g.Id && m.RemovedAt == null)).ShouldBe(1);

        // 系统消息已写入
        (await DbContext.Set<ChatMessage>().AnyAsync(m => m.ConversationId == g.Id && m.ContentType == MessageContentType.System)).ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateNotice_Owner_Should_Persist()
    {
        var g = (await Group.CreateGroupAsync(new CreateGroupDto { Title = "G", MemberIds = new List<Guid>() })).Data!;

        var r = await Group.UpdateNoticeAsync(g.Id, "  Welcome!  ");
        r.Succeeded.ShouldBeTrue(r.Message);

        var conv = await DbContext.Set<Conversation>().FindAsync(g.Id);
        conv!.Notice.ShouldBe("Welcome!");
    }

    [Fact]
    public async Task UpdateNotice_NonOwner_Should_Fail_403()
    {
        var g = (await Group.CreateGroupAsync(new CreateGroupDto { Title = "G", MemberIds = new List<Guid>() })).Data!;
        var conv = await DbContext.Set<Conversation>().FindAsync(g.Id);
        conv!.OwnerId = Guid.NewGuid();
        await DbContext.SaveChangesAsync();

        var r = await Group.UpdateNoticeAsync(g.Id, "No access");
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);
    }

    [Fact]
    public async Task Dissolve_Should_Soft_Delete_Conversation()
    {
        var g = (await Group.CreateGroupAsync(new CreateGroupDto { Title = "G", MemberIds = new List<Guid>() })).Data!;
        (await Group.DissolveGroupAsync(g.Id)).Succeeded.ShouldBeTrue();
        var conv = await DbContext.Set<Conversation>().IgnoreQueryFilters().FirstAsync(c => c.Id == g.Id);
        conv.IsDeleted.ShouldBeTrue();
    }
}
