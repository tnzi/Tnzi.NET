using PdfSharp.Fonts;

namespace Tnzi.Documents.Services.Internal;

/// <summary>
/// 盖章画文字用的 PDFsharp 字体解析器：从操作系统字体目录解析一款常规 sans。
/// </summary>
/// <remarks>
/// PDFsharp 6.x 不内置字体，要求进程级 <see cref="GlobalFontSettings.FontResolver"/>。本类：
/// <list type="bullet">
/// <item><b>只在没人装过解析器时才装自己的</b>。<c>FontResolver</c> 是**进程级单例**，
/// 同一进程里可能还有别的模块装了自己的（例如 <c>Tnzi.Finance.Documents</c> 的支票 MICR 解析器），
/// 覆盖掉别人的解析器会让对方的专用字体失效。反过来别人覆盖我们也无妨 ——
/// 那些解析器对未知字体族都会回退到常规 sans，画普通文字照常。</item>
/// <item>不内嵌任何字体二进制：避免把字体授权问题带进 MIT 框架
/// （同 <c>Tnzi.Imaging</c> 锁 ImageSharp 免授权版本的取舍）。</item>
/// </list>
/// 找不到系统字体且无人装过解析器时，画文字会以可读消息失败；只画图片/追加空页不受影响。
/// </remarks>
internal sealed class DocumentFontResolver : IFontResolver
{
    /// <summary>盖章文字使用的逻辑字体族名。</summary>
    public const string FamilyName = "TnziDocumentSans";

    private const string RegularFace = FamilyName + "#R";
    private const string BoldFace = FamilyName + "#B";

    private static readonly DocumentFontResolver Instance = new();
    private static readonly object Sync = new();

    private static byte[]? _regular;
    private static byte[]? _bold;
    private static string? _source;
    private static bool _probed;

    private DocumentFontResolver()
    {
    }

    /// <summary>
    /// 确保有可用的字体解析器；返回 false 表示当前进程画不了文字。
    /// </summary>
    /// <param name="logger">日志（可空）。</param>
    public static bool EnsureReady(ILogger? logger = null)
    {
        lock (Sync)
        {
            if (GlobalFontSettings.FontResolver == null)
            {
                Probe(logger);
                GlobalFontSettings.FontResolver = Instance;
            }

            // 别人的解析器在位：认为可用（未知字体族会回退到它自己的常规字体）。
            return !ReferenceEquals(GlobalFontSettings.FontResolver, Instance) || _regular != null;
        }
    }

    /// <summary>解析到的系统字体文件路径（未解析到为 null，诊断用）。</summary>
    public static string? ResolvedFontPath
    {
        get
        {
            lock (Sync)
            {
                return _source;
            }
        }
    }

    /// <inheritdoc />
    public byte[]? GetFont(string faceName) => faceName switch
    {
        BoldFace => _bold ?? _regular,
        _ => _regular
    };

    /// <inheritdoc />
    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => new(isBold ? BoldFace : RegularFace);

    private static void Probe(ILogger? logger)
    {
        if (_probed)
            return;
        _probed = true;

        foreach (var (regular, bold) in CandidateFonts())
        {
            if (!File.Exists(regular))
                continue;

            try
            {
                _regular = File.ReadAllBytes(regular);
                _source = regular;
                if (bold != null && File.Exists(bold))
                    _bold = File.ReadAllBytes(bold);

                logger?.LogInformation("PDF stamping resolved a system sans font from '{Path}'.", regular);
                return;
            }
            catch (IOException)
            {
                // 换下一个候选
            }
            catch (UnauthorizedAccessException)
            {
                // 换下一个候选
            }
        }

        logger?.LogWarning(
            "PDF stamping could not locate a system sans font; drawing text on PDFs will fail until one is installed.");
    }

    private static IEnumerable<(string Regular, string? Bold)> CandidateFonts()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var directory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (string.IsNullOrEmpty(directory))
                directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

            yield return (Path.Combine(directory, "arial.ttf"), Path.Combine(directory, "arialbd.ttf"));
            yield return (Path.Combine(directory, "segoeui.ttf"), Path.Combine(directory, "segoeuib.ttf"));
            yield return (Path.Combine(directory, "calibri.ttf"), Path.Combine(directory, "calibrib.ttf"));
            yield return (Path.Combine(directory, "verdana.ttf"), Path.Combine(directory, "verdanab.ttf"));
            yield return (Path.Combine(directory, "tahoma.ttf"), Path.Combine(directory, "tahomabd.ttf"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return ("/Library/Fonts/Arial.ttf", "/Library/Fonts/Arial Bold.ttf");
            yield return ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf");
            yield return ("/System/Library/Fonts/Helvetica.ttc", null);
        }
        else
        {
            yield return ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf");
            yield return ("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf");
            yield return ("/usr/share/fonts/liberation-sans/LiberationSans-Regular.ttf", "/usr/share/fonts/liberation-sans/LiberationSans-Bold.ttf");
            yield return ("/usr/share/fonts/dejavu/DejaVuSans.ttf", "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf");
            yield return ("/usr/share/fonts/TTF/DejaVuSans.ttf", "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf");
        }
    }
}
