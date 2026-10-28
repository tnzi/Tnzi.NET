using DocumentFormat.OpenXml.Packaging;
using Tnzi.AI.Rag.FileExtractors;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// Word 提取器把正文里每一处文字都带进分块：段落、表格（含嵌套表格）、块级内容控件，按文档顺序。
/// </summary>
/// <remarks>
/// ★ 此前只遍历正文的直接子段落：表格与块级内容控件整体跳过。以表格为主的规格表、价目表、表单检索不到，
/// 整篇都在表格里时提取为空、文档被标成失败。
/// </remarks>
public class WordFileExtractorTests
{
    [Fact]
    public async Task ExtractText_IncludesTableCells_RowByRow_InDocumentOrder()
    {
        var bytes = BuildDocx(body =>
        {
            body.Append(Paragraph("Price list"));
            body.Append(Table(
                Row(Cell("Item"), Cell("Price")),
                Row(Cell("Widget"), Cell("12.50"))));
            body.Append(Paragraph("Prices exclude tax."));
        });

        var text = await Extract(bytes);

        Lines(text).ShouldBe(["Price list", "Item | Price", "Widget | 12.50", "Prices exclude tax."]);
    }

    [Fact]
    public async Task ExtractText_TableOnlyDocument_IsNotEmpty()
    {
        var bytes = BuildDocx(body => body.Append(Table(Row(Cell("SKU-4411"), Cell("Stainless bracket")))));

        var text = await Extract(bytes);

        text.ShouldBe("SKU-4411 | Stainless bracket");
    }

    [Fact]
    public async Task ExtractText_IncludesBlockLevelContentControls()
    {
        var bytes = BuildDocx(body =>
        {
            body.Append(Paragraph("Before"));
            body.Append(new W.SdtBlock(new W.SdtContentBlock(Paragraph("Inside a content control"))));
            body.Append(Paragraph("After"));
        });

        var text = await Extract(bytes);

        Lines(text).ShouldBe(["Before", "Inside a content control", "After"]);
    }

    [Fact]
    public async Task ExtractText_NestedTable_StaysInsideItsCell()
    {
        var nested = Table(Row(Cell("inner a"), Cell("inner b")));
        var outerCell = new W.TableCell(Paragraph("outer"), nested, Paragraph(""));
        var bytes = BuildDocx(body => body.Append(Table(Row(Cell("left"), outerCell))));

        var text = await Extract(bytes);

        // 嵌套表格的行归它自己那张表，展开在外层单元格里，不另起一行、也不重复出现
        text.ShouldBe("left | outer inner a | inner b");
    }

    [Fact]
    public async Task ExtractText_SkipsEmptyRowsAndBlankParagraphs()
    {
        var bytes = BuildDocx(body =>
        {
            body.Append(Paragraph("   "));
            body.Append(Table(Row(Cell(""), Cell(" ")), Row(Cell("a"), Cell(""))));
        });

        var text = await Extract(bytes);

        // 全空的行不出现；有内容的行保留空单元格的位置，列不错位
        text.ShouldBe("a | ");
    }

    private static async Task<string> Extract(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return await new WordFileExtractor().ExtractTextAsync(stream, "doc.docx");
    }

    private static string[] Lines(string text) => text.Split(Environment.NewLine);

    private static byte[] BuildDocx(Action<W.Body> fill)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var body = new W.Body();
            fill(body);
            main.Document = new W.Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static W.Paragraph Paragraph(string text) => new(new W.Run(new W.Text(text)));

    private static W.TableCell Cell(string text) => new(Paragraph(text));

    private static W.TableRow Row(params W.TableCell[] cells) => new(cells);

    private static W.Table Table(params W.TableRow[] rows) => new(rows);
}
