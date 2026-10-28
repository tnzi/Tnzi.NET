namespace Tnzi.AI.Sandbox.Models;

/// <summary>
/// How a sandbox says that it cut a command's output at the configured cap.
/// </summary>
/// <remarks>
/// A cut that leaves no trace reads as the whole output: the agent then reasons over the
/// first half of a log as if it were all of it. Both providers append the same notice to
/// the stream they cut, so the cut is visible wherever the output ends up.
/// </remarks>
internal static class OutputTruncation
{
    public static string Notice(long limitBytes) => $"[output truncated: exceeded the {limitBytes}-byte limit]";

    public static string AppendNotice(string text, long limitBytes)
    {
        var notice = Notice(limitBytes);
        if (text.Length == 0) return notice;
        return text.EndsWith('\n') ? text + notice : $"{text}\n{notice}";
    }
}
