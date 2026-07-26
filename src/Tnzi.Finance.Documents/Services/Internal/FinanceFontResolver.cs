using System.Runtime.InteropServices;
using PdfSharp.Fonts;

namespace Tnzi.Finance.Documents.Services.Internal;

/// <summary>
/// PDFsharp 全局字体解析器（支票渲染）
/// </summary>
/// <remarks>
/// PDFsharp 6.x 不内置字体、要求进程级 <see cref="GlobalFontSettings.FontResolver"/>。因为不联网、
/// 也不便在源码内嵌 OFL 字体二进制，本解析器从操作系统字体目录解析一款常规 sans（Windows Fonts /
/// Linux fontconfig 常见路径 / macOS），并把 E-13B MICR 字体从 <c>Finance:CheckMicrFontPath</c> 加载。
/// 解析到的字体会 record 到日志（透出偏离）。该实现是进程级单例，首次渲染时安装一次。
/// </remarks>
internal sealed class FinanceFontResolver : IFontResolver
{
    /// <summary>常规 sans 逻辑字体族名</summary>
    public const string SansFamily = "TnziCheckSans";

    /// <summary>MICR 逻辑字体族名</summary>
    public const string MicrFamily = "TnziCheckMicr";

    private const string SansRegularFace = SansFamily + "#R";
    private const string SansBoldFace = SansFamily + "#B";
    private const string MicrFace = MicrFamily + "#R";

    private static readonly object Sync = new();
    private static FinanceFontResolver? _installed;
    private static byte[]? _sansRegular;
    private static byte[]? _sansBold;
    private static byte[]? _micr;
    private static string? _micrPath;
    private static string? _sansSource;
    private static bool _sansProbed;

    private FinanceFontResolver()
    {
    }

    /// <summary>安装进程级解析器（幂等；返回解析到的常规字体来源，null 表示未找到系统字体）。</summary>
    public static string? EnsureInstalled(ILogger? logger = null)
    {
        if (_installed == null)
        {
            lock (Sync)
            {
                if (_installed == null)
                {
                    ProbeSans(logger);
                    var resolver = new FinanceFontResolver();
                    GlobalFontSettings.FontResolver = resolver;
                    _installed = resolver;
                }
            }
        }

        return _sansSource;
    }

    /// <summary>是否有可用的常规字体（渲染前置校验）。</summary>
    public static bool HasSansFont
    {
        get
        {
            EnsureInstalled();
            return _sansRegular != null;
        }
    }

    /// <summary>
    /// 从配置路径加载 MICR 字体（按路径缓存）。返回 false 表示路径为空或文件不存在。
    /// </summary>
    public static bool TryLoadMicr(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        lock (Sync)
        {
            if (_micr != null && string.Equals(_micrPath, path, StringComparison.Ordinal))
                return true;
            if (!File.Exists(path))
                return false;

            _micr = File.ReadAllBytes(path);
            _micrPath = path;
            return true;
        }
    }

    public byte[]? GetFont(string faceName) => faceName switch
    {
        SansBoldFace => _sansBold ?? _sansRegular,
        SansRegularFace => _sansRegular,
        MicrFace => _micr,
        _ => _sansRegular
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        if (string.Equals(familyName, MicrFamily, StringComparison.OrdinalIgnoreCase))
            return new FontResolverInfo(MicrFace);
        return new FontResolverInfo(isBold ? SansBoldFace : SansRegularFace);
    }

    private static void ProbeSans(ILogger? logger)
    {
        if (_sansProbed)
            return;
        _sansProbed = true;

        foreach (var (regular, bold) in CandidateSansFonts())
        {
            if (!File.Exists(regular))
                continue;
            try
            {
                _sansRegular = File.ReadAllBytes(regular);
                _sansSource = regular;
                if (bold != null && File.Exists(bold))
                    _sansBold = File.ReadAllBytes(bold);
                logger?.LogInformation("Check rendering resolved a system sans font from '{Path}'.", regular);
                return;
            }
            catch (IOException)
            {
                // 尝试下一个候选
            }
        }

        logger?.LogWarning("Check rendering could not locate a system sans font; check PDF generation will fail until a font is available.");
    }

    /// <summary>按操作系统列出常规 sans 字体候选（regular, bold?）。</summary>
    private static IEnumerable<(string Regular, string? Bold)> CandidateSansFonts()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (string.IsNullOrEmpty(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

            yield return (Path.Combine(dir, "arial.ttf"), Path.Combine(dir, "arialbd.ttf"));
            yield return (Path.Combine(dir, "segoeui.ttf"), Path.Combine(dir, "segoeuib.ttf"));
            yield return (Path.Combine(dir, "calibri.ttf"), Path.Combine(dir, "calibrib.ttf"));
            yield return (Path.Combine(dir, "verdana.ttf"), Path.Combine(dir, "verdanab.ttf"));
            yield return (Path.Combine(dir, "tahoma.ttf"), Path.Combine(dir, "tahomabd.ttf"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return ("/Library/Fonts/Arial.ttf", "/Library/Fonts/Arial Bold.ttf");
            yield return ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf");
            yield return ("/System/Library/Fonts/Helvetica.ttc", null);
        }
        else
        {
            // Linux fontconfig 常见路径
            yield return ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf");
            yield return ("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf");
            yield return ("/usr/share/fonts/liberation-sans/LiberationSans-Regular.ttf", "/usr/share/fonts/liberation-sans/LiberationSans-Bold.ttf");
            yield return ("/usr/share/fonts/dejavu/DejaVuSans.ttf", "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf");
            yield return ("/usr/share/fonts/TTF/DejaVuSans.ttf", "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf");
        }
    }
}
