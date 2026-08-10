using PdfSharp.Fonts;
using Tnzi.Finance.Documents.Services.Internal;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 支票字体解析器接管进程级槽位时，被取代者的字体不得失效。
/// </summary>
/// <remarks>
/// <para>
/// PDFsharp 的 <c>GlobalFontSettings.FontResolver</c> 是<b>进程级单例</b>。
/// <c>Tnzi.Documents</c>（通用 PDF 盖章）刻意「只在没人装过时才装自己的」，
/// 而支票渲染<b>必须</b>占住它 —— MICR 磁码行要 E-13B 字形，别人的解析器对未知族一律
/// 回退常规 sans，于是磁码行画成 Arial：<b>屏幕上与纸上都完全正常，只有银行读头认不出来</b>。
/// </para>
/// <para>
/// 两个诉求靠<b>委派</b>同时满足：本解析器只处理自己的两个族，其余转交给被取代的那一个。
/// 这些用例锁的就是这条 —— 少了它，「依赖隔离」在运行期就是一句空话：
/// 两个包在编译期互不引用，却抢同一个静态字段。
/// </para>
/// <para>
/// ⚠️ 刻意<b>不</b>在测试里改写 <c>GlobalFontSettings.FontResolver</c>：同一程序集里的支票
/// 渲染测试要靠那个槽位真的出 PDF，并发改它会造出偶发失败。因此被测对象是直接构造出来的
/// 实例，而 <c>OwnsProcessResolver</c> 那三行引用比较（与 <c>PdfSharpCheckRenderer</c> 里
/// 依赖它的空白票纸守卫）留作已知的未覆盖点。
/// </para>
/// </remarks>
public class FinanceFontResolverDelegationTests
{
    /// <summary>MICR 族必须由本解析器自己接住，绝不转交。</summary>
    [Fact]
    public void Micr_family_is_never_delegated()
    {
        var previous = new RecordingResolver();
        var resolver = new FinanceFontResolver(previous);

        var info = resolver.ResolveTypeface(FinanceFontResolver.MicrFamily, isBold: false, isItalic: false);

        info.ShouldNotBeNull();
        info.FaceName.ShouldStartWith(FinanceFontResolver.MicrFamily);
        previous.ResolveCalls.ShouldBeEmpty();
    }

    /// <summary>支票自己的 sans 族同样自己接住。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Own_sans_family_is_never_delegated(bool isBold)
    {
        var previous = new RecordingResolver();
        var resolver = new FinanceFontResolver(previous);

        var info = resolver.ResolveTypeface(FinanceFontResolver.SansFamily, isBold, isItalic: false);

        info.ShouldNotBeNull();
        info.FaceName.ShouldStartWith(FinanceFontResolver.SansFamily);
        previous.ResolveCalls.ShouldBeEmpty();
    }

    /// <summary>
    /// ★ 别人的字体族转交给被取代的解析器，<b>并且</b>它给出的字面能取回它自己的字节。
    /// </summary>
    /// <remarks>
    /// 两步都要断言：只断第一步的话，「转交了族名但字节仍回退成我们的 sans」也一样绿 ——
    /// 而那正是「被覆盖无妨」这句话真正的漏洞所在（字形悄悄换掉了）。
    /// </remarks>
    [Fact]
    public void Foreign_family_is_delegated_and_so_are_its_font_bytes()
    {
        var previous = new RecordingResolver();
        var resolver = new FinanceFontResolver(previous);

        var info = resolver.ResolveTypeface("TnziDocumentSans", isBold: false, isItalic: false);

        info.ShouldNotBeNull();
        info.FaceName.ShouldBe(RecordingResolver.FaceName);
        previous.ResolveCalls.ShouldBe(["TnziDocumentSans"]);

        // 第二步：PDFsharp 拿着那个字面回来问**我们**（我们才是装在槽位里的那个）
        resolver.GetFont(info.FaceName).ShouldBe(RecordingResolver.FontBytes);
        previous.FontCalls.ShouldBe([RecordingResolver.FaceName]);
    }

    /// <summary>
    /// 没有被取代者（我们是第一个装的）时，未知族回退本解析器自己的 sans —— 与既有行为一致。
    /// </summary>
    [Fact]
    public void Without_a_previous_resolver_a_foreign_family_falls_back_to_our_own_sans()
    {
        var resolver = new FinanceFontResolver();

        var info = resolver.ResolveTypeface("SomeOtherFamily", isBold: false, isItalic: false);

        info.ShouldNotBeNull();
        info.FaceName.ShouldStartWith(FinanceFontResolver.SansFamily);
    }

    /// <summary>被取代者也不认这个族时，仍回退到本解析器自己的 sans 而不是抛。</summary>
    [Fact]
    public void A_previous_resolver_that_declines_still_falls_back_to_our_own_sans()
    {
        var resolver = new FinanceFontResolver(new DecliningResolver());

        var info = resolver.ResolveTypeface("SomeOtherFamily", isBold: false, isItalic: false);

        info.ShouldNotBeNull();
        info.FaceName.ShouldStartWith(FinanceFontResolver.SansFamily);
    }

    private sealed class RecordingResolver : IFontResolver
    {
        internal const string FaceName = "PreviousResolverFace";
        internal static readonly byte[] FontBytes = [0x42, 0x43, 0x44];

        internal List<string> ResolveCalls { get; } = [];
        internal List<string> FontCalls { get; } = [];

        public byte[]? GetFont(string faceName)
        {
            FontCalls.Add(faceName);
            return FontBytes;
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            ResolveCalls.Add(familyName);
            return new FontResolverInfo(FaceName);
        }
    }

    private sealed class DecliningResolver : IFontResolver
    {
        public byte[]? GetFont(string faceName) => null;

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) => null;
    }
}
