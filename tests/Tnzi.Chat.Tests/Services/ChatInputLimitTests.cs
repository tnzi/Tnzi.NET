namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 用户可控的长字符串越界时答 400，而不是把一次 <c>DbUpdateException</c> 交给用户。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>这一组挡的是生产才出现的 500。</b>SQL Server 与 PostgreSQL 对超出列宽的值直接拒绝
/// 整条插入；同一个模块里 <c>UpdateMemberSettingsAsync</c> 早就为备注与别名做了这件事，
/// 而正文、群名、群公告、广播四处一直没有。
/// </para>
/// <para>
/// ★ <b>为什么此前没有任何测试变红</b>：夹具跑 SQLite，而 SQLite <b>不强制</b>
/// <c>VARCHAR</c> 长度 —— 一条 10000 字的消息在这里插得进去。所以断言落在
/// <b>接口的答复</b>（400）与<b>库里有没有多出一行</b>上，两者与数据库提供程序无关。
/// </para>
/// </remarks>
public class ChatInputLimitTests : Integration.IntegrationTestBase
{
    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();
    private IGroupService Groups => ServiceProvider.GetRequiredService<IGroupService>();
    private IBroadcastService Broadcasts => ServiceProvider.GetRequiredService<IBroadcastService>();

    private static string Of(int length) => new('x', length);

    // ── 常量与列宽是同一个数字 ───────────────────────────────────────────────

    /// <summary>
    /// 代码校验到的长度就是列声明的长度。两处各写一个数字的话，漂开的那天不会有
    /// 任何东西变红 —— 除了这条。
    /// </summary>
    [Fact]
    public void The_declared_column_widths_are_the_ones_the_code_validates()
    {
        MaxLengthOf<ChatMessage>(nameof(ChatMessage.Content)).ShouldBe(ChatFieldLimits.MessageContent);
        MaxLengthOf<ChatMessage>(nameof(ChatMessage.Title)).ShouldBe(ChatFieldLimits.Title);
        MaxLengthOf<ChatMessage>(nameof(ChatMessage.LinkUrl)).ShouldBe(ChatFieldLimits.LinkUrl);
        MaxLengthOf<ChatMessage>(nameof(ChatMessage.Category)).ShouldBe(ChatFieldLimits.Category);
        MaxLengthOf<Conversation>(nameof(Conversation.Title)).ShouldBe(ChatFieldLimits.Title);
        MaxLengthOf<Conversation>(nameof(Conversation.Notice)).ShouldBe(ChatFieldLimits.Notice);
        MaxLengthOf<ConversationMember>(nameof(ConversationMember.Remark)).ShouldBe(ChatFieldLimits.MemberNote);
        MaxLengthOf<ConversationMember>(nameof(ConversationMember.Alias)).ShouldBe(ChatFieldLimits.MemberNote);
    }

    // ── 消息正文 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_oversized_message_is_refused_and_persists_nothing()
    {
        var conversationId = (await Conversations.GetOrCreateDirectAsync(Guid.NewGuid())).Data!.Id;

        var result = await Conversations.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.Text,
            Content = Of(ChatFieldLimits.MessageContent + 1)
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);

        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<ChatMessage>().AsNoTracking()
            .CountAsync(m => m.ConversationId == conversationId)).ShouldBe(0);
    }

    /// <summary>对照：正好等于上限的正文照常发得出去（不是把边界收窄了一格）。</summary>
    [Fact]
    public async Task A_message_exactly_at_the_limit_is_accepted()
    {
        var conversationId = (await Conversations.GetOrCreateDirectAsync(Guid.NewGuid())).Data!.Id;

        var result = await Conversations.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.Text,
            Content = Of(ChatFieldLimits.MessageContent)
        });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    // ── 群名与群公告 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task An_oversized_group_name_is_refused_at_creation()
    {
        var result = await Groups.CreateGroupAsync(new CreateGroupDto
        {
            Title = Of(ChatFieldLimits.Title + 1),
            MemberIds = [Guid.NewGuid()]
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task An_oversized_group_name_is_refused_on_rename()
    {
        var groupId = await NewGroupAsync();

        var result = await Groups.RenameGroupAsync(groupId, Of(ChatFieldLimits.Title + 1));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task An_oversized_group_notice_is_refused()
    {
        var groupId = await NewGroupAsync();

        var result = await Groups.UpdateNoticeAsync(groupId, Of(ChatFieldLimits.Notice + 1));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>对照：正常长度的群名与公告照常写得进去。</summary>
    [Fact]
    public async Task An_ordinary_group_name_and_notice_still_go_through()
    {
        var groupId = await NewGroupAsync();

        (await Groups.RenameGroupAsync(groupId, "Release crew")).Succeeded.ShouldBeTrue();
        (await Groups.UpdateNoticeAsync(groupId, "Ship on Friday")).Succeeded.ShouldBeTrue();
    }

    // ── 广播 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_oversized_broadcast_is_refused()
    {
        var result = await Broadcasts.BroadcastToUsersAsync(
            [Guid.NewGuid()], Of(ChatFieldLimits.MessageContent + 1));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>富通知的标题与链接走的是另外两列，同样要挡住。</summary>
    [Fact]
    public async Task An_oversized_notification_title_is_refused()
    {
        var result = await Broadcasts.NotifyUsersAsync([Guid.NewGuid()], new ChatNotification
        {
            Content = "ok",
            Title = Of(ChatFieldLimits.Title + 1)
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>对照：普通广播不受影响。</summary>
    [Fact]
    public async Task An_ordinary_broadcast_still_goes_through()
    {
        var result = await Broadcasts.BroadcastToUsersAsync([Guid.NewGuid()], "Maintenance at 21:00 UTC");

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(1);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private int MaxLengthOf<TEntity>(string property) where TEntity : class
        => DbContext.Model.FindEntityType(typeof(TEntity))!.FindProperty(property)!.GetMaxLength()
           ?? throw new InvalidOperationException($"{typeof(TEntity).Name}.{property} declares no max length.");

    private async Task<Guid> NewGroupAsync()
        => (await Groups.CreateGroupAsync(new CreateGroupDto
        {
            Title = "Crew",
            MemberIds = [Guid.NewGuid()]
        })).Data!.Id;
}
