namespace Tnzi.AI.Rag.FileExtractors;

/// <summary>
/// Word (.docx) 文件文本提取器，使用 OpenXml 库
/// </summary>
/// <remarks>
/// <para>
/// 按文档顺序遍历正文的块级元素：段落取其文本；表格每行输出一行、单元格以 <c>" | "</c> 分隔，单元格里的段落与嵌套表格
/// 展开在该单元格内；块级内容控件（<c>w:sdt</c>）、自定义 XML 块这类容器进入其内容继续遍历。
/// </para>
/// <para>
/// ★ 此前只取正文的直接子段落，表格与块级内容控件整体跳过：以表格为主的规格表、价目表、表单里的文字检索不到，
/// 整篇都在表格里时提取为空、文档被标成失败。
/// </para>
/// <para>
/// 页眉、页脚、脚注与批注不收：它们多是每页重复的样板或旁注，进入分块只会稀释检索。
/// </para>
/// </remarks>
public class WordFileExtractor : IFileExtractorService
{
    private static readonly HashSet<string> SupportedExtensions = [".docx"];

    /// <summary>同一行里相邻单元格之间的分隔符。</summary>
    private const string CellSeparator = " | ";

    /// <inheritdoc />
    public bool Supports(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext.ToLowerInvariant());
    }

    /// <inheritdoc />
    public Task<string> ExtractTextAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        using var document = WordprocessingDocument.Open(content, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return Task.FromResult(string.Empty);
        }

        var lines = new List<string>();
        AppendBlocks(body, lines, ct);

        return Task.FromResult(string.Join(Environment.NewLine, lines));
    }

    /// <summary>
    /// 按文档顺序收集一个块容器（正文、单元格、内容控件）里每个非空段落与表格行的文本，一项一行。
    /// </summary>
    private static void AppendBlocks(OpenXmlElement container, List<string> lines, CancellationToken ct)
    {
        foreach (var element in container.ChildElements)
        {
            ct.ThrowIfCancellationRequested();

            switch (element)
            {
                case Paragraph paragraph:
                    if (!string.IsNullOrWhiteSpace(paragraph.InnerText))
                    {
                        lines.Add(paragraph.InnerText);
                    }
                    break;
                case Table table:
                    AppendTableRows(table, lines, ct);
                    break;
                default:
                    // 块级内容控件（SdtBlock → SdtContentBlock）、自定义 XML 块等容器：进入其内容继续找段落与表格。
                    // 书签、节属性这类元素里没有段落，遍历它们什么都不产出。
                    if (element.HasChildren)
                    {
                        AppendBlocks(element, lines, ct);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// 表格每行一行，单元格以 <see cref="CellSeparator"/> 拼接；整行都空的行不输出，空单元格保留位置以免列错位。
    /// </summary>
    /// <remarks>
    /// 行与单元格可能包在行级 / 单元格级内容控件里，所以按后代找；只取「最近的那张表 / 那一行就是自己」的，
    /// 嵌套表格的行归它自己那张表，由所在单元格展开。
    /// </remarks>
    private static void AppendTableRows(Table table, List<string> lines, CancellationToken ct)
    {
        foreach (var row in table.Descendants<TableRow>().Where(r => r.Ancestors<Table>().First() == table))
        {
            ct.ThrowIfCancellationRequested();

            var cells = row.Descendants<TableCell>()
                .Where(c => c.Ancestors<TableRow>().First() == row)
                .Select(cell => CellText(cell, ct))
                .ToList();

            if (cells.Exists(text => text.Length > 0))
            {
                lines.Add(string.Join(CellSeparator, cells));
            }
        }
    }

    /// <summary>一个单元格是一个值：它的段落与嵌套表格的行以空格接在一起，留在所在行里。</summary>
    private static string CellText(TableCell cell, CancellationToken ct)
    {
        var cellLines = new List<string>();
        AppendBlocks(cell, cellLines, ct);
        return string.Join(" ", cellLines.Select(line => line.Trim()));
    }
}
