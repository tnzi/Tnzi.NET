using System.Text;
using static System.FormattableString;

namespace Tnzi.Documents.Tests;

/// <summary>
/// 手工拼一个最小合法 PDF（单页、Helvetica、若干条定位文本）。
/// </summary>
/// <remarks>
/// 刻意不用 PDFsharp 来造测试样本：PDFsharp 6.x 画字要进程级字体解析器 + 系统字体，
/// 那样测试就依赖机器上装了什么字体。这里用 PDF 内置的标准 14 字体 Helvetica，
/// 字形度量由 PdfPig 自带的 AFM 提供，任何机器上结果都一致。
/// </remarks>
internal static class TestPdfBuilder
{
    /// <summary>一条定位文本。坐标是 PDF 原生的（左下角原点，单位 point）。</summary>
    internal sealed record TextRun(string Text, double X, double Y, double FontSize = 12d);

    /// <summary>US Letter 页宽（point）。</summary>
    public const double LetterWidth = 612d;

    /// <summary>US Letter 页高（point）。</summary>
    public const double LetterHeight = 792d;

    /// <summary>拼一个 US Letter 单页 PDF。</summary>
    public static byte[] Letter(params TextRun[] runs) => Build(LetterWidth, LetterHeight, runs);

    /// <summary>拼一个指定尺寸的单页 PDF。</summary>
    public static byte[] Build(double pageWidth, double pageHeight, params TextRun[] runs)
    {
        var contentBytes = Encoding.ASCII.GetBytes(BuildContentStream(runs));

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"
        };

        using var stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n");
        // 二进制标记注释：告诉工具这不是纯文本文件
        stream.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        var offsets = new List<long>();
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, Invariant($"{index + 1} 0 obj\n{objects[index]}\nendobj\n"));
        }

        // 5 0 obj = 内容流
        offsets.Add(stream.Position);
        WriteAscii(stream, Invariant($"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n"));
        stream.Write(contentBytes);
        WriteAscii(stream, "\nendstream\nendobj\n");

        var xrefOffset = stream.Position;
        var size = offsets.Count + 1;

        // xref 每条记录必须正好 20 字节：10 位偏移 + 空格 + 5 位代数 + 空格 + 类型 + 空格 + 换行
        WriteAscii(stream, Invariant($"xref\n0 {size}\n"));
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets)
            WriteAscii(stream, Invariant($"{offset:D10} 00000 n \n"));

        WriteAscii(stream, Invariant($"trailer\n<< /Size {size} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n"));

        return stream.ToArray();
    }

    private static string BuildContentStream(IReadOnlyList<TextRun> runs)
    {
        var content = new StringBuilder();
        foreach (var run in runs)
        {
            content.Append("BT\n");
            content.Append(Invariant($"/F1 {run.FontSize} Tf\n"));
            content.Append(Invariant($"1 0 0 1 {run.X} {run.Y} Tm\n"));
            content.Append('(').Append(Escape(run.Text)).Append(") Tj\n");
            content.Append("ET\n");
        }

        return content.ToString();
    }

    private static string Escape(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal);

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
    }
}
