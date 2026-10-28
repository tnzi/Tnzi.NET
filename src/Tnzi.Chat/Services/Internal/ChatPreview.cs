namespace Tnzi.Chat.Services.Internal;

internal static class ChatPreview
{
    private const int MaxLength = 100;

    public static string Build(MessageContentType type, string? content) => type switch
    {
        MessageContentType.Image => "[Image]",
        MessageContentType.File => "[File]",
        _ => Truncate(content ?? string.Empty)
    };

    /// <remarks>
    /// 截断点落在代理对中间时整个字符一起舍掉：预览写进会话行，孤立的高代理项会让严格 UTF-8 的驱动（Npgsql）
    /// 拒绝这次写入，一条在第 100 个码元处带 emoji 的消息就发不出去。
    /// </remarks>
    private static string Truncate(string s) => s.FitToLength(MaxLength);
}
