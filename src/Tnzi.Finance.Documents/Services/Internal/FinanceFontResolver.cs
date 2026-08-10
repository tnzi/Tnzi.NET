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
///
/// <para>
/// ★★ <b>本解析器<u>必须</u>占住那个进程级槽位，不能像 <c>Tnzi.Documents</c> 那样「先判空才装」</b>：
/// 支票的 MICR 行要用 E-13B 字形，而别人的解析器对未知字体族一律回退常规 sans ——
/// 于是磁码行会被画成 Arial，<b>屏幕上与纸上都完全正常，只有银行的读头认不出来</b>。
/// 一批 20 张就是 20 个已消耗的支票号印在不可流通的纸上。
/// 所以这里的取舍与 <c>Tnzi.Documents</c> 相反，而两者<b>可以同时加载</b>。
/// </para>
/// <para>
/// ★ 代价用 <b>委派</b>补偿而不是靠「大家都回退 sans 就行」：安装时记下被取代的那一个
/// （<c>_previous</c>），凡不属于本解析器的字体族与字面一律转交给它。这样
/// <c>Tnzi.Documents</c> 的盖章文字仍然用它自己解析到的字体，而 MICR 仍然由我们保证。
/// 依赖隔离只管编译期与部署期的引用面，<b>管不了运行期的进程级状态</b>，这里是那条边界。
/// </para>
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

    /// <summary>被本解析器取代的那一个（不属于本解析器的字体族与字面转交给它）。</summary>
    private readonly IFontResolver? _previous;

    /// <summary>
    /// 生产路径只由 <see cref="EnsureInstalled"/> 调用；<c>internal</c> 是为了让委派行为
    /// 能在<b>不改动进程级 <see cref="GlobalFontSettings.FontResolver"/></b> 的前提下被测试
    /// —— 那个槽位同时被并发跑着的支票渲染测试依赖，测试里去动它会造出偶发失败。
    /// </summary>
    internal FinanceFontResolver(IFontResolver? previous = null)
    {
        _previous = previous;
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
                    // 记下被取代的那一个。本守卫保证只创建一个实例，故它不可能是自己，
                    // 委派不会自递归。
                    var previous = GlobalFontSettings.FontResolver;
                    var resolver = new FinanceFontResolver(previous);
                    if (previous != null)
                    {
                        logger?.LogInformation(
                            "Check rendering took over the process font resolver from {Previous}; unknown font families are delegated back to it.",
                            previous.GetType().Name);
                    }

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

    /// <summary>
    /// 本解析器是否仍然占着进程级槽位。
    /// </summary>
    /// <remarks>
    /// ★ MICR 行的正确性建立在这上面：若在我们之后又有别人装了自己的解析器，
    /// <c>MicrFamily</c> 会被那一个当作未知族回退成常规 sans —— 磁码行画成普通字形，
    /// 屏幕与纸面都看不出异常。所以打空白票纸之前必须问这一句。
    /// </remarks>
    public static bool OwnsProcessResolver
    {
        get
        {
            var installed = _installed;
            return installed != null && ReferenceEquals(GlobalFontSettings.FontResolver, installed);
        }
    }

    public byte[]? GetFont(string faceName) => faceName switch
    {
        SansBoldFace => _sansBold ?? _sansRegular,
        SansRegularFace => _sansRegular,
        MicrFace => _micr,
        // 不是我们发出的字面 → 是被取代者发出的，交回它去取字节（它的字体不该因为
        // 我们接管了槽位而失效）。它也不认时才回退本地 sans。
        _ => _previous?.GetFont(faceName) ?? _sansRegular
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        if (string.Equals(familyName, MicrFamily, StringComparison.OrdinalIgnoreCase))
            return new FontResolverInfo(MicrFace);
        if (string.Equals(familyName, SansFamily, StringComparison.OrdinalIgnoreCase))
            return new FontResolverInfo(isBold ? SansBoldFace : SansRegularFace);

        return _previous?.ResolveTypeface(familyName, isBold, isItalic)
            ?? new FontResolverInfo(isBold ? SansBoldFace : SansRegularFace);
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
