using System.Buffers.Binary;
using System.IO.Compression;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// Small, valid files the smoke tests hand to a real model: a solid-colour PNG and a one-page PDF with a line of text.
/// </summary>
/// <remarks>
/// Built byte by byte so the test project needs no imaging or PDF library. What they carry is chosen to be unambiguous
/// for a model to report back (a single colour, a made-up code word), so the assertions do not depend on wording.
/// </remarks>
internal static class SmokeFiles
{
    /// <summary>A PNG of the given size filled with one RGB colour.</summary>
    public static byte[] SolidPng(int width, int height, byte red, byte green, byte blue)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: truecolour

        // Every row starts with filter type 0 (none), then RGB triples.
        var raw = new byte[height * (1 + width * 3)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + width * 3);
            for (var x = 0; x < width; x++)
            {
                raw[row + 1 + x * 3] = red;
                raw[row + 2 + x * 3] = green;
                raw[row + 3 + x * 3] = blue;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>A one-page PDF that shows <paramref name="text"/> in Helvetica.</summary>
    /// <param name="text">ASCII letters, digits, spaces and simple punctuation (no parentheses or backslashes).</param>
    public static byte[] OneLinePdf(string text)
    {
        var content = $"BT /F1 28 Tf 72 700 Td ({text}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);

        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData, 4);
        png.Write(typeAndData);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        png.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
