using System.Text;
using PdfSharp.Pdf;
using static System.FormattableString;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 测试用的真 PDF：手工拼的多页文档（可带定位文本），以及一份带所有者密码（禁止编辑）的文档。
/// </summary>
/// <remarks>
/// 文本用 PDF 内置的标准 14 字体 Helvetica，字形度量由 PdfPig 自带，任何机器上读出来都一样；
/// 每一段文本是一次独立的 <c>Tj</c>，段与段之间<b>不画空格字形</b>，正是很多 PDF 生成器分词的方式，
/// 也就是锚文本匹配不能依赖空格的原因。
/// </remarks>
internal static class TestPdfs
{
    /// <summary>一段定位文本。坐标是 PDF 原生的（左下角原点，单位 point），页码从 1 开始。</summary>
    internal sealed record TextRun(string Text, double X, double Y, int Page = 1, double FontSize = 12d);

    private const double LetterWidth = 612d;
    private const double LetterHeight = 792d;

    /// <summary>一份 US Letter 的单页空白 PDF。</summary>
    public static readonly byte[] SinglePage = Pages(1);

    /// <summary>拼一份 <paramref name="pageCount"/> 页的 US Letter PDF，带若干段定位文本。</summary>
    public static byte[] Pages(int pageCount, params TextRun[] runs)
    {
        if (pageCount < 1)
            throw new ArgumentOutOfRangeException(nameof(pageCount));

        // 对象编号：1 目录、2 页树、3 字体；第 i 页（0 起）的页对象是 4 + 2i，内容流是 5 + 2i。
        var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => Invariant($"{4 + (2 * i)} 0 R")));
        var bodies = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii(Invariant($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>")),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
        };

        for (var i = 0; i < pageCount; i++)
        {
            var content = Ascii(ContentStream(runs.Where(r => r.Page == i + 1)));
            bodies.Add(Ascii(Invariant(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {LetterWidth} {LetterHeight}] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + (2 * i)} 0 R >>")));
            bodies.Add([.. Ascii(Invariant($"<< /Length {content.Length} >>\nstream\n")), .. content, .. Ascii("\nendstream")]);
        }

        using var stream = new MemoryStream();
        stream.Write(Ascii("%PDF-1.4\n"));
        stream.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        var offsets = new List<long>();
        for (var index = 0; index < bodies.Count; index++)
        {
            offsets.Add(stream.Position);
            stream.Write(Ascii(Invariant($"{index + 1} 0 obj\n")));
            stream.Write(bodies[index]);
            stream.Write(Ascii("\nendobj\n"));
        }

        var xref = stream.Position;
        var size = offsets.Count + 1;
        // xref 每条记录必须正好 20 字节。
        stream.Write(Ascii(Invariant($"xref\n0 {size}\n")));
        stream.Write(Ascii("0000000000 65535 f \n"));
        foreach (var offset in offsets)
            stream.Write(Ascii(Invariant($"{offset:D10} 00000 n \n")));
        stream.Write(Ascii(Invariant($"trailer\n<< /Size {size} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n")));

        return stream.ToArray();
    }

    /// <summary>
    /// 一份带所有者密码、禁止修改的单页 PDF（用户密码为空，所以谁都能打开来看）。
    /// 政府表格常见这种形态：读取方打得开，盖章方以修改模式打开时被拒。
    /// </summary>
    public static byte[] EditRestricted()
    {
        using var document = new PdfDocument();
        document.AddPage();
        document.SecuritySettings.OwnerPassword = "owner-secret";
        document.SecuritySettings.PermitModifyDocument = false;
        document.SecuritySettings.PermitAnnotations = false;
        document.SecuritySettings.PermitFormsFill = false;
        document.SecuritySettings.PermitAssembleDocument = false;

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static string ContentStream(IEnumerable<TextRun> runs)
    {
        var content = new StringBuilder();
        foreach (var run in runs)
        {
            content.Append("BT\n");
            content.Append(Invariant($"/F1 {run.FontSize} Tf\n"));
            content.Append(Invariant($"1 0 0 1 {run.X} {run.Y} Tm\n"));
            content.Append('(').Append(run.Text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") Tj\n");
            content.Append("ET\n");
        }

        return content.ToString();
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
}
