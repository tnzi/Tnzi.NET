using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Tnzi.Signing.Services.Internal;

/// <summary>对一份提交上来的签名图的判定。</summary>
internal enum SignatureImageVerdict
{
    /// <summary>一张完整的、盖章方解得开的 PNG 或 JPEG，尺寸在上限之内。</summary>
    Valid,

    /// <summary>不是 PNG / JPEG，或者是一张残缺、损坏的图。</summary>
    Malformed,

    /// <summary>文件头声明的像素尺寸超过 <see cref="SigningLimits.MaxSignatureImageSide"/> / <see cref="SigningLimits.MaxSignatureImagePixels"/>。</summary>
    TooLarge,

    /// <summary>一张完整的 PNG / JPEG，但是盖章方解不开的那几种变体（见 <see cref="SignatureImagePayload"/>）。</summary>
    Unsupported,
}

/// <summary>
/// 提交上来的签名图是不是一张<b>盖章方解得开的完整的图</b>：data URL（<c>data:image/png;base64,...</c>）或裸 base64，
/// 解出来是一张结构完整的 PNG 或 JPEG，且像素尺寸在上限之内。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>Tnzi.Documents</c> 盖章时的解码规则同口径（可带 data URL 前缀、允许折行空白、其余必须是合法 base64），
/// 认的格式也与它一致（<c>PdfSharpPdfStamper</c>：PNG 与 JPEG）。
/// 在提交那一刻校验而不是等到密封：签名图存进去之后收件人就是 <c>Signed</c>，<c>CheckSignable</c>
/// 不再接受他重交；若图盖不上去，密封会在盖章那一步失败，信封停在 <c>InProgress</c> —— 一次错误的提交
/// 就把整份请求钉死了（管理端现在有一个重新密封的动作，但那是给瞬时故障用的，它救不了一张永远解不出的图）。
/// </para>
/// <para>
/// ★ 只看文件头是不够的：<c>iVBORw0KGgo=</c>（只有 8 字节的 PNG 签名）、被签名板库或代理截断的 data URL、
/// CRC 对不上的图都以合法的文件头开始。所以 PNG 逐块走一遍（IHDR 在首、每块 CRC、至少一块 IDAT、以 IEND 结束），
/// 再把图像数据真的解压一遍，确认解出的字节够画满它声明的尺寸；JPEG 要有帧头（SOF）、扫描头（SOS）与结束标记（EOI）。
/// </para>
/// <para>
/// ★ 结构完整也不等于盖章方解得开。PDFsharp（<c>Tnzi.Documents</c> 的盖章实现）实测只认：位深 8 的 PNG（任一色彩类型，
/// 隔行与否均可），以及不隔行的 1 位 / 4 位调色板 PNG；JPEG 只认基线与渐进两种帧、8 位精度、灰度或 RGB（CMYK 不认）。
/// 1 / 2 / 4 / 16 位灰度、2 位调色板、隔行的 1 / 4 位调色板、所有 16 位 PNG 都在盖章那一步失败。
/// 这一组单独报 <see cref="SignatureImageVerdict.Unsupported"/>；<c>SubmitSigningPayloadTests</c> 用生产的盖章实现把
/// 每一种色彩类型 × 位深 × 隔行组合真的画一遍，两边判定必须一致（盖章实现换版本、支持面变了，那条测试先红）。
/// </para>
/// <para>
/// ★ 尺寸在<b>任何解压之前</b>按文件头里声明的宽高拦（<see cref="SigningLimits.MaxSignatureImageSide"/> /
/// <see cref="SigningLimits.MaxSignatureImagePixels"/>）：字符数上限管不住解码后的内存，一张不到 400 KB 的 PNG
/// 能声明 18000 × 18000 像素，密封时整张解码要分配上 GB，而每一次重新密封都会再来一遍。
/// 之后的解压只计数、不留存，内存是常量。
/// </para>
/// </remarks>
internal static class SignatureImagePayload
{
    private const string DataUrlPrefix = "data:";
    private const string Base64Marker = ";base64,";

    /// <summary>PNG 文件签名：<c>89 50 4E 47 0D 0A 1A 0A</c>。</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>JPEG 文件头：SOI（<c>FF D8</c>）后紧跟下一个标记的 <c>FF</c>。</summary>
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    /// <summary>JPEG 结束标记 EOI。</summary>
    private static readonly byte[] JpegEndOfImage = [0xFF, 0xD9];

    /// <summary>形态合法：非空、解得出字节、是一张完整的 PNG 或 JPEG，且尺寸在上限之内。</summary>
    public static bool IsWellFormed(string? value) => Inspect(value) == SignatureImageVerdict.Valid;

    /// <summary>判定一份签名图载荷。</summary>
    public static SignatureImageVerdict Inspect(string? value)
    {
        if (!TryDecode(value, out var bytes))
            return SignatureImageVerdict.Malformed;

        if (bytes.AsSpan().StartsWith(PngSignature))
            return InspectPng(bytes);

        if (bytes.AsSpan().StartsWith(JpegSignature))
            return InspectJpeg(bytes);

        return SignatureImageVerdict.Malformed;
    }

    private static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var payload = value.Trim();
        if (payload.StartsWith(DataUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var marker = payload.IndexOf(Base64Marker, StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                return false;

            payload = payload[(marker + Base64Marker.Length)..];
        }

        payload = payload
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (payload.Length == 0)
            return false;

        var buffer = new byte[payload.Length];
        if (!Convert.TryFromBase64String(payload, buffer, out var written) || written == 0)
            return false;

        bytes = buffer[..written];
        return true;
    }

    private static bool WithinLimits(long width, long height)
        => width <= SigningLimits.MaxSignatureImageSide
           && height <= SigningLimits.MaxSignatureImageSide
           && width * height <= SigningLimits.MaxSignatureImagePixels;

    // ── PNG ──────────────────────────────────────────────────────────────

    private static SignatureImageVerdict InspectPng(byte[] bytes)
    {
        var offset = PngSignature.Length;
        PngHeader? header = null;
        var sawPalette = false;
        var idat = new List<(int Offset, int Length)>();

        while (true)
        {
            if (!TryReadChunk(bytes, offset, out var type, out var dataOffset, out var length))
                return SignatureImageVerdict.Malformed;
            offset = dataOffset + length + 4;

            if (header is null)
            {
                // 首块必须是 IHDR；尺寸在这里就拦，早于任何解压。
                if (type != "IHDR" || !PngHeader.TryParse(bytes.AsSpan(dataOffset, length), out var parsed))
                    return SignatureImageVerdict.Malformed;
                if (!WithinLimits(parsed.Width, parsed.Height))
                    return SignatureImageVerdict.TooLarge;
                if (!parsed.IsStampable)
                    return SignatureImageVerdict.Unsupported;
                header = parsed;
                continue;
            }

            switch (type)
            {
                case "PLTE":
                    sawPalette = true;
                    break;
                case "IDAT":
                    idat.Add((dataOffset, length));
                    break;
                case "IEND":
                    // 调色板图没有 PLTE 就没有颜色可查；没有 IDAT 就没有像素。
                    if (idat.Count == 0 || (header.Value.ColorType == 3 && !sawPalette))
                        return SignatureImageVerdict.Malformed;
                    return InflatesToAtLeast(bytes, idat, header.Value.ExpectedDataLength())
                        ? SignatureImageVerdict.Valid
                        : SignatureImageVerdict.Malformed;
            }
        }
    }

    /// <summary>读一块：长度、类型、数据位置；越界或 CRC 不对返回 false。</summary>
    private static bool TryReadChunk(byte[] bytes, int offset, out string type, out int dataOffset, out int length)
    {
        type = string.Empty;
        dataOffset = 0;
        length = 0;

        if (offset > bytes.Length - 12)
            return false;

        var declared = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        if (declared > (uint)(bytes.Length - offset - 12))
            return false;

        length = (int)declared;
        dataOffset = offset + 8;
        var crc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(dataOffset + length, 4));
        if (crc != PngCrc.Compute(bytes.AsSpan(offset + 4, length + 4)))
            return false;

        type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
        return true;
    }

    /// <summary>
    /// 把 IDAT 拼起来解压，解出的字节是否够画满声明的尺寸。只计数不留存：内存与图的大小无关。
    /// </summary>
    private static bool InflatesToAtLeast(byte[] bytes, List<(int Offset, int Length)> idat, long expected)
    {
        using var compressed = new MemoryStream();
        foreach (var (offset, length) in idat)
            compressed.Write(bytes, offset, length);
        compressed.Position = 0;

        try
        {
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
            var buffer = new byte[16 * 1024];
            long total = 0;
            int read;
            while (total < expected && (read = inflater.Read(buffer, 0, buffer.Length)) > 0)
                total += read;
            return total >= expected;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private readonly record struct PngHeader(long Width, long Height, int BitDepth, int ColorType, bool Interlaced)
    {
        public static bool TryParse(ReadOnlySpan<byte> data, out PngHeader header)
        {
            header = default;
            if (data.Length != 13)
                return false;

            var width = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
            var height = BinaryPrimitives.ReadUInt32BigEndian(data[4..8]);
            int bitDepth = data[8], colorType = data[9], compression = data[10], filter = data[11], interlace = data[12];

            var validDepth = colorType switch
            {
                0 => bitDepth is 1 or 2 or 4 or 8 or 16,
                3 => bitDepth is 1 or 2 or 4 or 8,
                2 or 4 or 6 => bitDepth is 8 or 16,
                _ => false,
            };
            if (width == 0 || height == 0 || !validDepth || compression != 0 || filter != 0 || interlace > 1)
                return false;

            header = new PngHeader(width, height, bitDepth, colorType, interlace == 1);
            return true;
        }

        /// <summary>盖章方（PDFsharp）解得开的组合：位深 8，或不隔行的 1 位 / 4 位调色板。</summary>
        public bool IsStampable => BitDepth == 8 || (ColorType == 3 && BitDepth is 1 or 4 && !Interlaced);

        /// <summary>按尺寸、位深、色彩类型、隔行方式算出的解压后数据长度（每行一个过滤字节）。</summary>
        public long ExpectedDataLength()
        {
            var channels = ColorType switch { 2 => 3, 4 => 2, 6 => 4, _ => 1 };
            var bitsPerPixel = (long)channels * BitDepth;

            long Pass(long width, long height) => width == 0 || height == 0 ? 0 : height * (1 + ((width * bitsPerPixel) + 7) / 8);

            if (!Interlaced)
                return Pass(Width, Height);

            // Adam7 的七趟：(起点 x, 起点 y, 步长 x, 步长 y)。
            ReadOnlySpan<(int X, int Y, int Dx, int Dy)> passes =
                [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
            long total = 0;
            foreach (var (x, y, dx, dy) in passes)
            {
                var w = Width > x ? (Width - x + dx - 1) / dx : 0;
                var h = Height > y ? (Height - y + dy - 1) / dy : 0;
                total += Pass(w, h);
            }

            return total;
        }
    }

    /// <summary>PNG 块校验用的 CRC-32（多项式 0xEDB88320，PNG 规范附录的那一个）。</summary>
    private static class PngCrc
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (var n = 0u; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }

            return table;
        }
    }

    // ── JPEG ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 逐个走标记段直到扫描头（SOS）：途中要遇到帧头（SOF，读出宽高），SOS 之后要有结束标记（EOI）。
    /// </summary>
    /// <remarks>
    /// 盖章方只认基线与渐进两种帧（SOF0 / SOF2）、8 位精度、1 个（灰度）或 3 个（RGB）分量：
    /// 签名板与浏览器导出的 JPEG 都在其中；其余帧类型（扩展、无损、算术编码、分层）与 CMYK 报 <see cref="SignatureImageVerdict.Unsupported"/>。
    /// 熵编码数据里的 <c>FF</c> 一律跟着 <c>00</c> 或复位标记，所以 SOS 之后出现的 <c>FF D9</c> 就是真正的 EOI。
    /// </remarks>
    private static SignatureImageVerdict InspectJpeg(byte[] bytes)
    {
        var offset = 2;
        (long Width, long Height)? frame = null;

        while (true)
        {
            if (offset >= bytes.Length || bytes[offset] != 0xFF)
                return SignatureImageVerdict.Malformed;

            // 标记前可以有任意个填充用的 FF。
            while (offset < bytes.Length && bytes[offset] == 0xFF)
                offset++;
            if (offset >= bytes.Length)
                return SignatureImageVerdict.Malformed;

            var marker = bytes[offset++];
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
                continue;
            if (marker is 0xD8 or 0xD9)
                return SignatureImageVerdict.Malformed;

            if (offset > bytes.Length - 2)
                return SignatureImageVerdict.Malformed;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
            if (length < 2 || length > bytes.Length - offset)
                return SignatureImageVerdict.Malformed;
            var segment = bytes.AsSpan(offset, length);
            offset += length;

            if (marker is 0xC0 or 0xC2)
            {
                if (segment.Length < 8)
                    return SignatureImageVerdict.Malformed;
                int precision = segment[2];
                long height = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(3, 2));
                long width = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(5, 2));
                int components = segment[7];
                if (width == 0 || height == 0)
                    return SignatureImageVerdict.Malformed;
                if (!WithinLimits(width, height))
                    return SignatureImageVerdict.TooLarge;
                if (precision != 8 || components is not (1 or 3))
                    return SignatureImageVerdict.Unsupported;
                frame = (width, height);
                continue;
            }

            // 其余的帧类型（扩展、无损、算术编码、分层）。
            if (marker is >= 0xC1 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return SignatureImageVerdict.Unsupported;

            if (marker == 0xDA)
            {
                if (frame is null)
                    return SignatureImageVerdict.Malformed;
                return bytes.AsSpan(offset).IndexOf(JpegEndOfImage) >= 0
                    ? SignatureImageVerdict.Valid
                    : SignatureImageVerdict.Malformed;
            }
        }
    }
}
