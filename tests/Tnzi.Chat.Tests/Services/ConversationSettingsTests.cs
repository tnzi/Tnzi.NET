
namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 集成测试：成员设置（sticky/remark）、清空历史、会话内搜索。
/// 使用真实 DbContext + Repository，对 presence 用 Mock（非此测试重点）。
/// </summary>
public class ConversationSettingsTests : Integration.IntegrationTestBase
{
    private IConversationService Service => ServiceProvider.GetRequiredService<IConversationService>();

    // ── sticky ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMemberSettings_Sticky_Should_Set_IsSticky_And_Sort_First()
    {
        // 建两个会话：普通会话 + 要置顶的会话
        var convA = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;
        // 给 A 一个较早 LastMessageAt，让它在默认排序排在 B 后
        var convRow = await DbContext.Set<Conversation>().FindAsync(convA.Id);
        convRow!.LastMessageAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await DbContext.SaveChangesAsync();

        var convB = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;
        var convBRow = await DbContext.Set<Conversation>().FindAsync(convB.Id);
        convBRow!.LastMessageAt = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        await DbContext.SaveChangesAsync();

        // 未置顶时 B 排前（更晚的消息）
        var before = (await Service.GetMyConversationsAsync()).Data!;
        before[0].Id.ShouldBe(convB.Id);

        // 置顶 A
        var r = await Service.UpdateMemberSettingsAsync(convA.Id, new ConversationMemberSettingsDto { IsSticky = true });
        r.Succeeded.ShouldBeTrue(r.Message);

        // DB 确认
        var member = await DbContext.Set<ConversationMember>()
            .FirstAsync(m => m.ConversationId == convA.Id && m.UserId == CurrentUserId);
        member.IsSticky.ShouldBeTrue();

        // 列表里 A 排在第一，IsSticky=true
        var after = (await Service.GetMyConversationsAsync()).Data!;
        after[0].Id.ShouldBe(convA.Id);
        after[0].IsSticky.ShouldBeTrue();
    }

    // ── remark ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMemberSettings_Remark_Should_Persist_And_ClearOnEmptyString()
    {
        var conv = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;

        // 设置备注
        (await Service.UpdateMemberSettingsAsync(conv.Id, new ConversationMemberSettingsDto { Remark = "Best Friend" }))
            .Succeeded.ShouldBeTrue();

        var member = await DbContext.Set<ConversationMember>()
            .FirstAsync(m => m.ConversationId == conv.Id && m.UserId == CurrentUserId);
        member.Remark.ShouldBe("Best Friend");

        // 列表项应返回 Remark
        var list = (await Service.GetMyConversationsAsync()).Data!;
        list.First(i => i.Id == conv.Id).Remark.ShouldBe("Best Friend");

        // 空字符串 → 清除备注
        (await Service.UpdateMemberSettingsAsync(conv.Id, new ConversationMemberSettingsDto { Remark = "" }))
            .Succeeded.ShouldBeTrue();

        var reloaded = await DbContext.Set<ConversationMember>()
            .FirstAsync(m => m.ConversationId == conv.Id && m.UserId == CurrentUserId);
        reloaded.Remark.ShouldBeNull();
    }

    // ── clear history ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearHistory_Should_Filter_Old_Messages_And_Show_New_Ones()
    {
        var conv = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;

        // 插入一条旧消息
        await Service.SendMessageAsync(conv.Id, new SendMessageDto { Content = "old message" });

        // 确认旧消息可见
        var before = (await Service.GetMessagesAsync(conv.Id, new MessageThreadQueryDto())).Data!;
        before.Messages.Count.ShouldBe(1);

        // 清空历史：水位打到当前时刻
        (await Service.ClearHistoryAsync(conv.Id)).Succeeded.ShouldBeTrue();

        // DB 确认 ClearedAt 已设置
        var member = await DbContext.Set<ConversationMember>()
            .FirstAsync(m => m.ConversationId == conv.Id && m.UserId == CurrentUserId);
        member.ClearedAt.ShouldNotBeNull();

        // GetMessages 不再返回旧消息
        var afterClear = (await Service.GetMessagesAsync(conv.Id, new MessageThreadQueryDto())).Data!;
        afterClear.Messages.ShouldBeEmpty("messages before ClearedAt must be hidden");

        // 清空后发一条新消息 → 可见
        // 微小等待确保 SentAt > ClearedAt（同毫秒概率极低，但做个显式错开）
        await Task.Delay(10);
        await Service.SendMessageAsync(conv.Id, new SendMessageDto { Content = "new message" });
        var afterNew = (await Service.GetMessagesAsync(conv.Id, new MessageThreadQueryDto())).Data!;
        afterNew.Messages.Count.ShouldBe(1);
        afterNew.Messages[0].Content.ShouldBe("new message");
    }

    // ── search ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchMessages_Should_Return_Only_Matching_Text_Messages()
    {
        var conv = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;

        await Service.SendMessageAsync(conv.Id, new SendMessageDto { Content = "foo bar" });
        await Service.SendMessageAsync(conv.Id, new SendMessageDto { Content = "hello world" });
        await Service.SendMessageAsync(conv.Id, new SendMessageDto { Content = "foobar rocks" });

        var result = await Service.SearchMessagesAsync(conv.Id, "foo", new MessageThreadQueryDto());
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Messages.Count.ShouldBe(2);
        result.Data.Messages.ShouldAllBe(m => m.Content.Contains("foo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchMessages_Should_Respect_ClearedAt_Filter()
    {
        var conv = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;

        await Service.SendMessageAsync(conv.Id, new SendMessageDto { Content = "foo before clear" });

        await Service.ClearHistoryAsync(conv.Id);

        await Task.Delay(10);
        await Service.SendMessageAsync(conv.Id, new SendMessageDto { Content = "foo after clear" });

        // 只能搜出清空后的消息
        var result = await Service.SearchMessagesAsync(conv.Id, "foo", new MessageThreadQueryDto());
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Messages.Count.ShouldBe(1);
        result.Data.Messages[0].Content.ShouldBe("foo after clear");
    }

    [Fact]
    public async Task SearchMessages_NonMember_Should_Return_403()
    {
        // 直接建一个会话，当前用户不参与
        var conv = new Conversation { Type = ConversationType.Direct, DirectKey = "z:y", MemberCount = 2 };
        DbContext.Set<Conversation>().Add(conv);
        await DbContext.SaveChangesAsync();

        var r = await Service.SearchMessagesAsync(conv.Id, "foo", new MessageThreadQueryDto());
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);
    }

    [Fact]
    public async Task UpdateMemberSettings_NonMember_Should_Return_403()
    {
        // Conversation where the current user is NOT a member.
        var conv = new Conversation { Type = ConversationType.Direct, DirectKey = "w:v", MemberCount = 2 };
        DbContext.Set<Conversation>().Add(conv);
        await DbContext.SaveChangesAsync();

        var r = await Service.UpdateMemberSettingsAsync(conv.Id, new ConversationMemberSettingsDto { IsSticky = true });
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);
    }
}
