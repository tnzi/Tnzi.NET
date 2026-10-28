using System.Text.RegularExpressions;
using Tnzi.TestBase;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// 根目录 <c>Directory.Build.props</c> 压掉了 ImageSharp 3.1.12 的五条 NuGet 审计告警，
/// 每一条的理由都是「本仓不可达」或「已在仓内缓解」。这里把那些理由变成可执行的断言。
/// </summary>
/// <remarks>
/// <para>
/// 压制本身不会过期：哪天有人调了 <c>HistogramEqualization()</c>、写了一个 TIFF 编码器，
/// 或者在 <see cref="ImageDecodeGuard"/> 之外新开了一个直接 <c>Image.Load</c> 的入口，
/// 构建输出照样干干净净，被压住的告警恰好就是那时该响的那一条。
/// 所以理由一旦不再成立，必须是这里变红，而不是什么都不发生。
/// </para>
/// <para>
/// 只扫 <c>src/</c>：发布出去的是它。测试代码为了造夹具会用到编码器，那不进任何消费方的进程。
/// 整行注释不算（文档里提到 <c>Image.Load</c> 不是调用）。
/// </para>
/// </remarks>
public class ImageSharpAdvisoryGateTests
{
    /// <summary>当前仓内把外来字节直接交给 ImageSharp 解码的全部位置。</summary>
    /// <remarks>
    /// 两处都在交出字节之前拒绝 BigTIFF。新增一处时先让它也这么做，再把它加进这张表。
    /// </remarks>
    private static readonly string[] KnownDecodeSites =
    [
        "src/Tnzi.Imaging/Services/ImageDecodeGuard.cs",
        "src/Tnzi.Imaging/Services/QrCodeReader.cs",
    ];

    /// <summary>
    /// <c>Image.Load</c> / <c>Image.Identify</c> 及其异步与泛型形态；直接取解码器实例
    /// （如 <c>TiffDecoder.Instance.Decode</c>）同样绕开入口，一并算在内。
    /// </summary>
    private static readonly Regex DecodeCall = new(
        @"\bImage\.(Load|LoadAsync|Identify|IdentifyAsync)\s*(<[^<>()]*>)?\s*\(|\b\w+Decoder\.Instance\.(Decode|Identify)",
        RegexOptions.Compiled);

    private static readonly Lazy<IReadOnlyList<SourceFile>> Sources = new(LoadSources);

    [Fact]
    public void DirectDecodeCalls_LiveOnlyWhereBigTiffIsRejectedFirst()
    {
        var sites = Sources.Value
            .Where(file => file.CodeLines.Any(DecodeCall.IsMatch))
            .Select(file => file.Path)
            .Order(StringComparer.Ordinal)
            .ToList();

        sites.ShouldBe(
            KnownDecodeSites,
            ignoreOrder: false,
            customMessage: "A new direct ImageSharp decode call appeared (or a known one moved). ImageSharp 3.1 loops on a crafted BigTIFF "
            + "(GHSA-wmxv-xphr-5c9g, suppressed in Directory.Build.props as mitigated): route the bytes through "
            + "ImageDecodeGuard, or call ImageDecodeGuard.ThrowIfBigTiff before handing them over, then update this list.");

        foreach (var site in sites)
        {
            Sources.Value.Single(file => file.Path == site).CodeLines
                .ShouldContain(line => line.Contains("ThrowIfBigTiff"), $"{site} decodes without rejecting BigTIFF first.");
        }
    }

    /// <summary>
    /// 被压制的另外四条告警，各自只在调用某个 API 时可达；这些 API 在 <c>src/</c> 里必须一次都不出现。
    /// </summary>
    [Theory]
    [InlineData("HistogramEqualization", "GHSA-j3p4-wp97-rph4")]
    [InlineData("TiffEncoder", "GHSA-j9gm-c75j-xc9q / GHSA-jjfr-hcj7-qf5w")]
    [InlineData("SaveAsTiff", "GHSA-j9gm-c75j-xc9q / GHSA-jjfr-hcj7-qf5w")]
    [InlineData("TiffFormat", "GHSA-j9gm-c75j-xc9q / GHSA-jjfr-hcj7-qf5w")]
    [InlineData("IccProfile", "GHSA-gwg2-r3hj-4w44")]
    public void SuppressedAdvisories_StayUnreachable(string identifier, string advisory)
    {
        // 正控制：扫描确实读到了会编码图片的那些文件，否则下面的「一个都没有」什么也没证明
        Sources.Value.ShouldContain(
            file => file.CodeLines.Any(line => line.Contains("SaveAsJpegAsync")),
            "The source scan found no image-encoding code at all, so an empty result below would prove nothing.");

        // 只锚定开头：SaveAsTiff 要连 SaveAsTiffAsync 一起算，IccProfile 要连 IccProfileHeader 一起算
        var pattern = new Regex($@"\b{identifier}");
        var hits = Sources.Value
            .Where(file => file.CodeLines.Any(pattern.IsMatch))
            .Select(file => file.Path)
            .ToList();

        hits.ShouldBeEmpty(
            $"{identifier} is what makes {advisory} reachable, and that advisory is suppressed in Directory.Build.props "
            + "on the grounds that nothing here uses it. Remove the suppression (and get a patched ImageSharp) before using it.");
    }

    private static IReadOnlyList<SourceFile> LoadSources()
    {
        var root = RepoRoot.Locate();

        return RepoScan.EnumerateFiles("src", "*.cs")
            .Select(path => new SourceFile(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToList()))
            .ToList();
    }

    private sealed record SourceFile(string Path, IReadOnlyList<string> CodeLines);
}
