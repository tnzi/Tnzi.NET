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
/// <para>
/// ★ 「字节级」是按字节读写换来的，不是按文本：文件按自己的 BOM 判编码（UTF-8 / UTF-16 / UTF-32 带 BOM，无 BOM 按 UTF-8），
/// 严格解码后<b>再编回去比一遍</b>，逐字节一致才动它，写回时带上原来的 BOM、用原来的编码。此前走
/// <c>ReadAllText</c> / <c>WriteAllText</c>：带 BOM 的文件每跑一次少 3 个字节，UTF-16 文件被整体转成 UTF-8，
/// GBK / Shift-JIS 这类文件里的非 ASCII 字节被永久替换成 U+FFFD。判不出能逐字节还原的编码时<b>拒绝注入</b>
/// （抛 <see cref="IOException"/>，运行以「工作区布置失败」结束），而不是跳过 brief 静默开跑：
/// 绑定选了注入 Agent 指令，少了它跑出来的就不是那个 Agent。
/// </para>
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

    /// <summary>新建记忆文件用的编码：UTF-8、无 BOM（与此前一致，CLI 自己写出的记忆文件也是这样）。</summary>
    private static readonly UTF8Encoding NewFileEncoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// 写入或更新受管 brief 块。
    /// </summary>
    /// <param name="fileSystem">记忆文件所在的文件系统（执行目标的）。</param>
    /// <param name="path">记忆文件路径。</param>
    /// <param name="brief">受管块内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次是否<b>创建</b>了该文件（清理时据此决定删文件还是切块）。</returns>
    public static async Task<bool> WriteAsync(ICliHostFileSystem fileSystem, string path, string brief, CancellationToken cancellationToken)
    {
        Check.NotNull(fileSystem);
        Check.NotNullOrWhiteSpace(path);

        var block = MarkerBegin + "\n" + (brief ?? string.Empty).TrimEnd('\n') + "\n" + MarkerEnd + "\n";

        if (!await fileSystem.FileExistsAsync(path, cancellationToken))
        {
            await fileSystem.WriteAllBytesAsync(path, NewFileEncoding.GetBytes(block), cancellationToken);
            return true;
        }

        var file = await fileSystem.ReadAllBytesAsync(path, cancellationToken);
        if (!RoundTripText.TryDecode(file, out var existing))
        {
            throw UnsupportedEncoding(fileSystem, path);
        }

        var updated = TryLocateBlock(existing.Text, out var start, out var end)
            ? existing.Text[..start] + block + existing.Text[end..]
            : existing.Text + ManagedSeparator + block;

        await fileSystem.WriteAllBytesAsync(path, existing.Encode(updated), cancellationToken);
        return false;
    }

    /// <summary>
    /// 确认一个已存在的记忆文件能被注入并逐字节还原；不能则抛 <see cref="IOException"/>。文件不存在时什么都不做。
    /// </summary>
    /// <remarks>
    /// 工作区布置在往用户目录写任何东西之前先问这一句：注入注定被拒的运行，不该先在用户仓库里留下 <c>.tnzi</c> 再失败。
    /// </remarks>
    public static async Task EnsureInjectableAsync(ICliHostFileSystem fileSystem, string path, CancellationToken cancellationToken)
    {
        Check.NotNull(fileSystem);
        Check.NotNullOrWhiteSpace(path);

        if (await fileSystem.FileExistsAsync(path, cancellationToken)
            && !RoundTripText.TryDecode(await fileSystem.ReadAllBytesAsync(path, cancellationToken), out _))
        {
            throw UnsupportedEncoding(fileSystem, path);
        }
    }

    private static IOException UnsupportedEncoding(ICliHostFileSystem fileSystem, string path)
        => new($"'{fileSystem.Paths.GetFileName(path)}' in the working directory is not in an encoding the agent brief can be added to " +
               "and removed from byte for byte (UTF-8, or UTF-16 / UTF-32 with a byte order mark). The file was left untouched " +
               "and the run was not started. Convert the file to UTF-8, or turn off agent instruction injection for this binding.");

    /// <summary>
    /// 切除受管块，把文件还原到写入前的字节。
    /// </summary>
    /// <param name="fileSystem">记忆文件所在的文件系统（执行目标的）。</param>
    /// <param name="path">记忆文件路径。</param>
    /// <param name="createdByUs">本次运行是否创建了该文件（true 则直接删文件）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task CleanupAsync(ICliHostFileSystem fileSystem, string path, bool createdByUs, CancellationToken cancellationToken)
    {
        Check.NotNull(fileSystem);

        if (!await fileSystem.FileExistsAsync(path, cancellationToken))
        {
            return;
        }

        if (createdByUs)
        {
            await fileSystem.DeleteFileAsync(path, cancellationToken);
            return;
        }

        // 解不出能逐字节还原的文本 = 不是我们注入过的样子（注入时同一判据已经拒绝过），不碰。
        if (!RoundTripText.TryDecode(await fileSystem.ReadAllBytesAsync(path, cancellationToken), out var existing)
            || !TryLocateBlock(existing.Text, out var start, out var end))
        {
            // 没有受管块 = 这里从没被注入过，不碰。
            return;
        }

        // 连同前面的固定分隔符一起切掉。分隔符是我们加的，属于受管区。
        var text = existing.Text;
        var prefixEnd = start;
        if (start >= ManagedSeparator.Length
            && text.AsSpan(start - ManagedSeparator.Length, ManagedSeparator.Length).SequenceEqual(ManagedSeparator))
        {
            prefixEnd = start - ManagedSeparator.Length;
        }

        // 不做 TrimEnd、不补换行、不因"剩下的是空白"就删文件 ——
        // 那些"顺手整理"每一条都会在用户仓库里留下一个 diff。
        await fileSystem.WriteAllBytesAsync(path, existing.Encode(text[..prefixEnd] + text[end..]), cancellationToken);
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

    /// <summary>
    /// 一个能逐字节还原的已解码文件：原样的 BOM + 原来的编码 + 严格解码出的文本。
    /// </summary>
    private sealed class RoundTripText
    {
        private readonly byte[] _preamble;
        private readonly Encoding _encoding;

        private RoundTripText(byte[] preamble, Encoding encoding, string text)
        {
            _preamble = preamble;
            _encoding = encoding;
            Text = text;
        }

        public string Text { get; }

        /// <summary>用文件原来的 BOM 与编码编码一段文本。</summary>
        public byte[] Encode(string text) => [.. _preamble, .. _encoding.GetBytes(text)];

        /// <summary>
        /// 按 BOM 判编码、严格解码，并确认编回去与原字节逐一相同；任何一步不成立都返回 false。
        /// </summary>
        /// <remarks>
        /// UTF-32 LE 的 BOM 以 UTF-16 LE 的 BOM 开头，所以先认 4 字节的再认 2 字节的。
        /// 编回去再比一遍不是多余的：严格解码只保证「没有非法字节」，保证不了「换个写法编出来还是这些字节」。
        /// </remarks>
        public static bool TryDecode(byte[] bytes, [NotNullWhen(true)] out RoundTripText? decoded)
        {
            var (encoding, preambleLength) = Detect(bytes);
            var body = bytes.AsSpan(preambleLength);

            string text;
            try
            {
                text = encoding.GetString(body);
            }
            catch (DecoderFallbackException)
            {
                decoded = null;
                return false;
            }

            if (!encoding.GetBytes(text).AsSpan().SequenceEqual(body))
            {
                decoded = null;
                return false;
            }

            decoded = new RoundTripText(bytes[..preambleLength], encoding, text);
            return true;
        }

        private static (Encoding Encoding, int PreambleLength) Detect(byte[] bytes) => bytes switch
        {
            [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(false, true), 3),
            [0xFF, 0xFE, 0x00, 0x00, ..] => (new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true), 4),
            [0x00, 0x00, 0xFE, 0xFF, ..] => (new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true), 4),
            [0xFF, 0xFE, ..] => (new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), 2),
            [0xFE, 0xFF, ..] => (new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), 2),
            _ => (NewFileEncoding, 0)
        };
    }
}
