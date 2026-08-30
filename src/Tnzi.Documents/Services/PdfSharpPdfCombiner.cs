using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Tnzi.Documents.Services;

/// <summary>
/// 默认的 PDF 合并实现（PDFsharp 6.x，纯托管、MIT）。
/// </summary>
/// <remarks>
/// <para><b>不需要字体，也不需要外部进程。</b>合并只是把页对象搬进一份新文档，
/// 与 <see cref="PdfSharpPdfStamper"/> 那种要画字的操作不同，精简容器里照样能跑。</para>
/// <para><b>源文档必须以 <see cref="PdfDocumentOpenMode.Import"/> 打开。</b>
/// PDFsharp 只在 Import 模式下允许把页搬到别的文档里；用 <c>Modify</c> 打开再 <c>AddPage</c>
/// 会在运行时抛 —— 这一条没有编译期提示，是本类最容易改错的地方。</para>
/// <para><b>承载字节的流要活到最后一页搬完。</b>PDFsharp 按需读对象，流一旦提前释放，
/// 症状是搬到某一页时冒出 <see cref="ObjectDisposedException"/>，而不是打开那一刻就失败
/// （<see cref="PdfSharpPdfStamper"/> 里同样把流留在文档的作用域外层，理由一样）。</para>
/// </remarks>
public sealed class PdfSharpPdfCombiner : IPdfCombiner
{
    /// <inheritdoc />
    public byte[] Combine(IReadOnlyList<byte[]> documents)
    {
        Check.NotNull(documents);

        if (documents.Count == 0)
        {
            throw new ArgumentException(
                "Combine needs at least one document; a zero-page PDF is not a valid document.",
                nameof(documents));
        }

        // 一份就是答案本身：原样返回，不让 PDFsharp 重写一遍文件结构。
        // 合法性仍然要验，否则"一份"会成为绕过检查的口子。
        if (documents.Count == 1)
        {
            OpenAndDispose(documents[0], 0);
            return documents[0];
        }

        using var output = new PdfDocument();
        for (var index = 0; index < documents.Count; index++)
        {
            RequireBytes(documents[index], index);

            using var input = new MemoryStream(documents[index], writable: false);
            using var source = Open(input, index);
            RequirePages(source, index);

            // AddPage(PdfPage) 在页属于别的文档时走导入；MediaBox 与 /Rotate 一并带过来，
            // 所以横竖混排的输入出来仍是横竖混排。
            for (var page = 0; page < source.PageCount; page++)
            {
                output.AddPage(source.Pages[page]);
            }
        }

        using var stream = new MemoryStream();
        output.Save(stream);
        return stream.ToArray();
    }

    /// <summary>单份输入的合法性检查：打开、看有没有页、随即释放。</summary>
    private static void OpenAndDispose(byte[] document, int index)
    {
        RequireBytes(document, index);

        using var input = new MemoryStream(document, writable: false);
        using var opened = Open(input, index);
        RequirePages(opened, index);
    }

    private static void RequireBytes(byte[] document, int index)
    {
        if (document == null || document.Length == 0)
        {
            throw new PdfDocumentException($"Document at index {index} is empty.");
        }
    }

    /// <summary>以 Import 模式打开一份输入，并把底层异常包成框架异常（带上是第几份）。</summary>
    private static PdfDocument Open(Stream input, int index)
    {
        try
        {
            return PdfReader.Open(input, PdfDocumentOpenMode.Import);
        }
        catch (Exception ex) when (ex is not TnziException)
        {
            throw new PdfDocumentException(
                $"Document at index {index} is not a readable PDF: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 一页都没有的输入要报错而不是安静跳过：合并出来的份数对不上是最难查的那种问题
    /// （产物看着正常，只是少了一份，而没有任何一步失败过）。
    /// </summary>
    private static void RequirePages(PdfDocument document, int index)
    {
        if (document.PageCount == 0)
        {
            throw new PdfDocumentException($"Document at index {index} has no pages.");
        }
    }
}
