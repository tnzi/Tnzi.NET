namespace Tnzi.AI.Cli.Workspace;

/// <summary>
/// 把受管 brief 写进 provider 的记忆文件，且<b>能字节级回滚</b>。
/// </summary>
/// <remarks>
/// <para>
/// 为什么不能直接覆写：<c>WorkDirectoryMode = UserProvided</c> 时，工作目录就是用户自己的
/// 代码仓库，那里很可能<b>已经有</b>一个 <c>CLAUDE.md</c> / <c>AGENTS.md</c>。
/// 参考实现早期就是无条件写文件，把用户仓库里的记忆文件整个截断了 —— 这类事故一旦发生，
/// 用户丢的是自己写了很久的内容。
/// </para>
/// <para>三种状态，各自有明确的回滚语义：</para>
/// <list type="bullet">
/// <item><b>文件不存在</b> → 只写 marker 块，无前导分隔符。清理时整个删掉文件，
/// 目录列表回到布置前的样子。</item>
/// <item><b>文件存在、无 marker</b> → 追加<b>固定</b>分隔符 + marker 块。
/// 分隔符的字节算受管区，清理时连它一起切掉，于是用户原文件的尾部字节
/// （没有换行、一个换行、还是三个换行）原样还原 —— 不做任何"归一化"。</item>
/// <item><b>文件存在、有 marker</b> → 原地替换块内容，块外一字不动。
/// 同一工作目录反复运行不会让文件无限增长。</item>
/// </list>
/// </remarks>
public static class BriefMarkerWriter
{
    /// <summary>受管区起始标记。</summary>
    public const string MarkerBegin = "<!-- TNZI:AGENT-BRIEF:BEGIN (auto-managed; do not edit) -->";

    /// <summary>受管区结束标记。</summary>
    public const string MarkerEnd = "<!-- TNZI:AGENT-BRIEF:END -->";

    /// <summary>
    /// 用户内容与受管区之间的<b>固定</b>分隔符。
    /// </summary>
    /// <remarks>
    /// 宽度固定是回滚能做到字节级的前提：若按用户文件原有的尾部换行数量自适应，
    /// 清理时就得反推"原本有几个换行"，每次运行都会留下一点点无法消除的 diff。
    /// </remarks>
    public const string ManagedSeparator = "\n\n";

    /// <summary>
    /// 写入或更新受管 brief 块。
    /// </summary>
    /// <returns>本次是否<b>创建</b>了该文件（清理时据此决定删文件还是切块）。</returns>
    public static async Task<bool> WriteAsync(string path, string brief, CancellationToken cancellationToken)
    {
        Check.NotNullOrWhiteSpace(path);

        var block = MarkerBegin + "\n" + (brief ?? string.Empty).TrimEnd('\n') + "\n" + MarkerEnd + "\n";

        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(path, block, cancellationToken);
            return true;
        }

        var existing = await File.ReadAllTextAsync(path, cancellationToken);
        if (TryLocateBlock(existing, out var start, out var end))
        {
            await File.WriteAllTextAsync(path, existing[..start] + block + existing[end..], cancellationToken);
            return false;
        }

        await File.WriteAllTextAsync(path, existing + ManagedSeparator + block, cancellationToken);
        return false;
    }

    /// <summary>
    /// 切除受管块，把文件还原到写入前的字节。
    /// </summary>
    /// <param name="path">记忆文件路径。</param>
    /// <param name="createdByUs">本次运行是否创建了该文件（true 则直接删文件）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task CleanupAsync(string path, bool createdByUs, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (createdByUs)
        {
            File.Delete(path);
            return;
        }

        var existing = await File.ReadAllTextAsync(path, cancellationToken);
        if (!TryLocateBlock(existing, out var start, out var end))
        {
            // 没有受管块 = 这里从没被注入过，不碰。
            return;
        }

        // 连同前面的固定分隔符一起切掉。分隔符是我们加的，属于受管区。
        var prefixEnd = start;
        if (start >= ManagedSeparator.Length
            && existing.AsSpan(start - ManagedSeparator.Length, ManagedSeparator.Length).SequenceEqual(ManagedSeparator))
        {
            prefixEnd = start - ManagedSeparator.Length;
        }

        // 不做 TrimEnd、不补换行、不因"剩下的是空白"就删文件 ——
        // 那些"顺手整理"每一条都会在用户仓库里留下一个 diff。
        await File.WriteAllTextAsync(path, existing[..prefixEnd] + existing[end..], cancellationToken);
    }

    /// <summary>
    /// 定位受管块的 <c>[start, end)</c> 字节范围。
    /// </summary>
    /// <remarks>
    /// 结束标记只在起始标记<b>之后</b>搜索，这样两种畸形输入才不会失控：
    /// <list type="bullet">
    /// <item>用户内容里有一段孤立的结束标记（比如文档里展示这个格式），
    /// 朴素的双 <c>IndexOf</c> 会判定"没有块"，于是每次运行都在文件末尾再追加一块，
    /// 文件无限增长；</item>
    /// <item>上一次运行在写入中途崩溃，只留下半个块。把「有起始、无结束」当作
    /// "块一直延伸到文件末尾"，下一次写入就能原地替换掉那半块，而不是在它下面再叠一块。</item>
    /// </list>
    /// </remarks>
    private static bool TryLocateBlock(string content, out int start, out int end)
    {
        start = content.IndexOf(MarkerBegin, StringComparison.Ordinal);
        if (start < 0)
        {
            end = 0;
            return false;
        }

        var afterBegin = start + MarkerBegin.Length;
        var relativeEnd = content.IndexOf(MarkerEnd, afterBegin, StringComparison.Ordinal);
        if (relativeEnd < 0)
        {
            end = content.Length;
            return true;
        }

        end = relativeEnd + MarkerEnd.Length;
        if (end < content.Length && content[end] == '\n')
        {
            end++;
        }

        return true;
    }
}
