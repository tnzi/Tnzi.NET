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

    private static string Truncate(string s) => s.Length <= MaxLength ? s : s[..MaxLength];
}
