namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 存根附加行的条数与长度上限，以及越界时该怎么办。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么在这里而不在渲染器里</b>：<c>ICheckDocumentRenderer</c> 与
/// <see cref="ICheckStubLineProvider"/> 都是<b>可替换的扩展点</b>，校验写进某一个实现里，
/// 换一个实现就绕过去了。<c>CheckService</c> / <c>CheckBatchComposer</c> 才是附加行
/// <b>进入渲染请求</b>的那一道边界，是唯一不依赖「每个实现都记得做」的位置
/// （与 <c>ReceiptFieldLimits</c> 同一条判据）。
/// </para>
/// <para>
/// ★★ <b>越界一律归一化，不拒绝</b> —— 这与收据的人工输入路径相反，是刻意的：
/// 附加行是<b>装饰性</b>的（付款正确性完全不依赖它），而此刻支票号已经分配、
/// 登记簿行已经写下。为了一条太长的案卷说明把整批付款打不出来，代价方向是错的。
/// 所以：太长的截断（给人看的文本，留开头有用）、空白的丢弃、超量的截到上限。
/// </para>
/// <para>
/// ★ 上限来自<b>物理空间</b>而不是数据库列宽（附加行不落库）：存根 95.25mm 高，
/// 8pt 行距约 4.5mm，既有固定行已用掉约 40mm，余下约 36mm 装得下 8 行。
/// 再多就会顶穿存根边界，把下一联挤变形——那种失效在屏幕预览上还看得见，
/// 打到纸上就是一叠废票。
/// </para>
/// </remarks>
internal static class CheckStubLineLimits
{
    /// <summary>每张支票的附加行条数上限。</summary>
    internal const int MaxLines = 8;

    /// <summary>标签最大长度（存根表格左列 34mm，8pt 下约 30 字）。</summary>
    internal const int MaxLabelLength = 40;

    /// <summary>值最大长度（右列约 150mm；留出换行余量）。</summary>
    internal const int MaxValueLength = 160;

    /// <summary>
    /// 把消费应用给的附加行收敛到存根排得下的形状。
    /// </summary>
    /// <param name="raw">原样传入（不被修改）；null 视为没有附加行。</param>
    /// <returns>归一化后的<b>新</b>列表；没有可用行时是空列表，绝不返回 null。</returns>
    internal static List<CheckStubLine> Normalize(IEnumerable<CheckStubLine>? raw)
    {
        if (raw == null)
            return [];

        var normalized = new List<CheckStubLine>();
        foreach (var line in raw)
        {
            // 整条为空的行只会在存根上留一道空白，不如不排。
            // （只有标签、没有值是有意义的，那是一行分节标题。）
            if (line == null || string.IsNullOrWhiteSpace(line.Label))
                continue;

            normalized.Add(new CheckStubLine(
                Cut(line.Label.Trim(), MaxLabelLength),
                string.IsNullOrWhiteSpace(line.Value) ? null : Cut(line.Value.Trim(), MaxValueLength)));

            if (normalized.Count == MaxLines)
                break;
        }

        return normalized;
    }

    /// <summary>
    /// 按 UTF-16 码元数截断，且不留下半个代理对。
    /// </summary>
    /// <remarks>
    /// 附加行不落库，所以这里防的不是插入失败而是<b>渲染产物</b>：
    /// 孤立的高代理项是非法 UTF-16，写进 HTML 会得到一个替换字符，
    /// 而 PdfSharp 的字形查表会直接抛。同 <c>ReceiptFieldLimits.Cut</c>。
    /// </remarks>
    private static string Cut(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        var end = maxLength;
        if (char.IsHighSurrogate(text[end - 1]))
            end--;

        return text[..end];
    }
}
