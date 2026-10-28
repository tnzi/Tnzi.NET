using Tnzi.Chat.Tests.Integration;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 会话的「最后一条消息」预览截到 100 个码元：截断点落在代理对中间时整个字符一起舍掉。
/// </summary>
/// <remarks>
/// 预览写进会话行。孤立的高代理项会让严格 UTF-8 的驱动（Npgsql）拒绝这次写入：发消息答 500，
/// 广播则让那位收件人的送达失败。SQLite 不检查，所以这里直接看落进去的字符串。
/// </remarks>
public class ChatPreviewTruncationTests : IntegrationTestBase
{
    /// <summary>第 100 个码元是 emoji 的前半个。</summary>
    private static readonly string EmojiAtTheCut = new string('a', 99) + "\U0001F600tail";

    [Fact]
    public void Build_CutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        ChatPreview.Build(MessageContentType.Text, EmojiAtTheCut).ShouldBe(new string('a', 99));
    }

    [Fact]
    public async Task Broadcast_CutInsideASurrogatePair_StoresAPreviewWithoutALoneSurrogate()
    {
        var user = Guid.NewGuid();
        var broadcasts = ServiceProvider.GetRequiredService<IBroadcastService>();

        (await broadcasts.BroadcastToUsersAsync([user], EmojiAtTheCut)).Data.ShouldBe(1);

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
        var conversation = await db.Set<Conversation>().AsNoTracking().SingleAsync(c => c.DirectKey == $"system:{user:N}");
        conversation.LastMessagePreview.ShouldBe(new string('a', 99));
    }
}
