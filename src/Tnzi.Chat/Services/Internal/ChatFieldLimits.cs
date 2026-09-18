namespace Tnzi.Chat.Services.Internal;

/// <summary>
/// 用户可控字符串的长度上限 —— <b>实体配置与写入校验共用同一组常量</b>。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>为什么必须校验。</b>越界值在 SQL Server / PostgreSQL 上是一次
/// <c>DbUpdateException</c>，也就是给用户一个 500；正确的答复是 400。
/// 同一个模块里 <c>UpdateMemberSettingsAsync</c> 早就为备注与别名做了这件事，
/// 而正文、群名、群公告三条一直没有 —— 同一文件内的不一致比缺失本身更容易看见。
/// </para>
/// <para>
/// ★★★ <b>为什么测试挡不住它。</b>集成测试夹具跑 SQLite，而 SQLite <b>不强制</b>
/// <c>VARCHAR</c> 长度：一条 10000 字的消息在测试里插得进去、断言全绿，
/// 到了生产才第一次报错。所以这里的常量由<b>实体配置直接引用</b>，
/// 让「代码校验到的长度」与「列声明的长度」不可能漂开，而不是各写一个数字。
/// </para>
/// <para>
/// ★ 判定落在<b>要落库的那个值</b>上：会先 <c>Trim()</c> 的字段就拿修剪后的长度去比，
/// 否则一段两侧带大量空白的输入会被判成越界，而它其实存得下。
/// </para>
/// </remarks>
internal static class ChatFieldLimits
{
    /// <summary>消息正文（<c>ChatMessage.Content</c>）。</summary>
    internal const int MessageContent = 4000;

    /// <summary>富通知标题（<c>ChatMessage.Title</c>）与会话标题（<c>Conversation.Title</c>）。</summary>
    internal const int Title = 200;

    /// <summary>富通知跳转链接（<c>ChatMessage.LinkUrl</c>）。</summary>
    internal const int LinkUrl = 2000;

    /// <summary>富通知分类（<c>ChatMessage.Category</c>）。</summary>
    internal const int Category = 100;

    /// <summary>群公告（<c>Conversation.Notice</c>）。</summary>
    internal const int Notice = 2000;

    /// <summary>会话内的备注与别名（<c>ConversationMember.Remark</c> / <c>Alias</c>）。</summary>
    internal const int MemberNote = 100;

    /// <summary>
    /// 附件文件名（<c>ChatMessage.FileName</c>）。调用方原样给出，客户端可任意构造 ——
    /// 09-04 那批长度校验唯一漏掉的用户可控字符串。
    /// </summary>
    internal const int FileName = 512;

    /// <summary>附件文件引用（<c>ChatMessage.FileId</c>）。写入前要求能解析成 <c>Guid</c>，故天然有界。</summary>
    internal const int FileId = 256;

    /// <summary>广播审计的来源标签（<c>BroadcastLog.Source</c>），由调用模块填。</summary>
    internal const int BroadcastSource = 128;

    /// <summary>
    /// 越界时给出一条面向用户的说明，合法时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="value">**即将落库的那个值**（该 Trim 的先 Trim）。</param>
    /// <param name="max">列宽。</param>
    /// <param name="field">出现在提示里的字段名。</param>
    internal static string? Exceeded(string? value, int max, string field)
        => value != null && value.Length > max
            ? $"{field} is too long (max {max} characters)."
            : null;
}
