using System.Globalization;

namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 这一轮的文件附件怎样告诉 agent：一段追加在这一轮上下文里的说明，写明每个文件的名字、大小、类型与位置。
/// </summary>
/// <remarks>
/// 文件不作为内容块交给模型。不写这段说明，agent 只从用户的话里看到「见附件」，会以为自己已经看过、或者说没看到附件；
/// 写明了它才知道要用自己的工具去打开。没有位置的文件（消费方没有放到执行目标上）照样列出，并说明它不在 agent 读得到的地方，
/// 免得 agent 在工作目录里四处找。
/// </remarks>
internal static class CliAttachedFiles
{
    /// <summary>把说明追加到这一轮的上下文后面（没有文件时原样返回）。</summary>
    public static string? AppendTo(string? turnContext, IReadOnlyList<CliPromptFile> files)
    {
        var note = Describe(files);
        if (note is null)
        {
            return turnContext;
        }

        return string.IsNullOrWhiteSpace(turnContext) ? note : $"{turnContext}\n\n{note}";
    }

    /// <summary>这些文件的说明；没有文件时为 null。</summary>
    public static string? Describe(IReadOnlyList<CliPromptFile> files)
    {
        if (files.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder();
        text.Append("The user attached ")
            .Append(files.Count == 1 ? "a file" : $"{files.Count} files")
            .Append(" to this message. ")
            .Append(files.Count == 1 ? "It was" : "They were")
            .Append(" not shown to you inline: open ")
            .Append(files.Count == 1 ? "it" : "them")
            .AppendLine(" with your tools before answering about the content.");

        foreach (var file in files)
        {
            text.Append("- ").Append(file.FileName)
                .Append(" (").Append(FormatSize(file.Size)).Append(", ").Append(file.MediaType).Append(')');
            text.AppendLine(string.IsNullOrWhiteSpace(file.Path)
                ? ": not placed where you can read it; tell the user if you need its content."
                : $" at {file.Path}");
        }

        return text.ToString().TrimEnd();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB")
    };
}
