using PDFtoImage;

namespace Tnzi.Documents.Services.Internal;

/// <summary>
/// 「PDFium 的原生库这台机器上加载得起来吗」——问一次，记住答案。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>「引用了包」和「原生库在」是两件事。</b>本能力真实的失效方式是后者：
/// 发布时没带 <c>runtimes/&lt;rid&gt;/native</c>、RID 不匹配、精简容器里缺 C 运行时。
/// 这三种情况下程序集引用看起来完全正常，直到第一次渲染才炸。
/// 调用方要决定「把入口显示出来吗」，得到的必须是这一台机器上的事实。
/// </para>
/// <para>
/// 探测方式是真渲染一份最小 PDF：只查文件在不在没有意义 ——
/// 加载失败的原因经常是依赖的 C 运行时缺失，那时文件明明在那里。
/// </para>
/// </remarks>
internal static class NativeProbe
{
    /// <summary>一份最小的合法单页 PDF（1×1 点的空页）。</summary>
    /// <remarks>内联而不是放资源文件：它是探测器的一部分，分开放只会让两者慢慢对不上。</remarks>
    private const string MinimalPdf =
        "%PDF-1.4\n"
        + "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n"
        + "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n"
        + "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 1 1]>>endobj\n"
        + "trailer<</Root 1 0 R>>\n";

    private static bool? _result;
    private static readonly Lock Gate = new();

    /// <summary>原生库加载得起来吗。</summary>
    /// <param name="logger">加载不起来时把原因记下来 —— 否则这里只会返回一个没有解释的 false。</param>
    public static bool Succeeded(ILogger logger)
    {
        if (_result is { } cached)
        {
            return cached;
        }

        lock (Gate)
        {
            if (_result is { } inner)
            {
                return inner;
            }

            _result = Probe(logger);
            return _result.Value;
        }
    }

    private static bool Probe(ILogger logger)
    {
        try
        {
            Conversion.GetPageCount(System.Text.Encoding.ASCII.GetBytes(MinimalPdf));
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException)
        {
            logger.LogWarning(
                ex,
                "PDF rasterisation is unavailable: the PDFium native library could not be loaded. "
                + "Publish with a runtime identifier, or make sure runtimes/<rid>/native travels with the application.");
            return false;
        }
        catch (Exception ex)
        {
            // 原生库加载起来了，只是这份探测 PDF 没被接受 —— 那不影响真实文档，报可用。
            logger.LogDebug(ex, "The PDFium availability probe failed on its sample document but the native library loaded.");
            return true;
        }
    }
}
