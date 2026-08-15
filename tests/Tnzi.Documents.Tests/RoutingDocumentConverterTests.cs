using System.Text;

namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="RoutingDocumentConverter"/> 的分流规则与可用性判定。
/// </summary>
/// <remarks>
/// 这些规则只体现在几行选择逻辑里，改错了编译照过：分流挑错引擎的症状是「PDF 出来了，
/// 只是排版全丢」，而可用性判错的症状是「说能预览，点开 500」—— 两种都不会让别的测试变红。
/// </remarks>
public class RoutingDocumentConverterTests
{
    [Fact]
    public async Task ConvertToPdfAsync_HtmlGoesToTheFirstEngineThatClaimsIt()
    {
        var html = new StubConverter(".html");
        var office = new StubConverter(".docx");
        var router = new RoutingDocumentConverter(html, office);

        await router.ConvertToPdfAsync([1], "composed.html");

        html.Calls.ShouldBe(1);
        office.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task ConvertToPdfAsync_NonHtmlIsUntouchedByTheHtmlEngine()
    {
        var html = new StubConverter(".html");
        var office = new StubConverter(".docx");
        var router = new RoutingDocumentConverter(html, office);

        await router.ConvertToPdfAsync([1], "contract.docx");

        office.Calls.ShouldBe(1);
        html.Calls.ShouldBe(0);
    }

    [Fact]
    public void CanConvert_IsTheUnionOfWhatTheEnginesClaim()
    {
        var router = new RoutingDocumentConverter(new StubConverter(".html"), new StubConverter(".docx"));

        router.CanConvert("a.html").ShouldBeTrue();
        router.CanConvert("a.docx").ShouldBeTrue();
        router.CanConvert("a.png").ShouldBeFalse();
        router.CanConvert("").ShouldBeFalse();
    }

    /// <summary>
    /// ★ 「装了浏览器、没装 LibreOffice」的宿主上，<c>.docx</c> 必须答「不可用」。
    /// </summary>
    /// <remarks>
    /// 这正是 <c>IsAvailableFor</c> 存在的理由：<c>IsAvailable</c> 只能回答「有没有任何引擎能干活」，
    /// 拿它去判断某个具体格式，就会在这台机器上给出肯定答复，然后在用户点开预览时炸成 500。
    /// </remarks>
    [Fact]
    public void IsAvailableFor_AsksTheEngineThatWouldActuallyDoTheWork()
    {
        var html = new StubConverter(".html") { Available = true };
        var office = new StubConverter(".docx") { Available = false };
        var router = new RoutingDocumentConverter(html, office);

        router.IsAvailable.ShouldBeTrue();          // 有引擎能干活
        router.IsAvailableFor("a.html").ShouldBeTrue();
        router.IsAvailableFor("a.docx").ShouldBeFalse();
        router.IsAvailableFor("a.png").ShouldBeFalse();
    }

    [Fact]
    public void IsAvailable_IsFalseOnlyWhenNoEngineCanWork()
    {
        new RoutingDocumentConverter(
                new StubConverter(".html") { Available = false },
                new StubConverter(".docx") { Available = false })
            .IsAvailable.ShouldBeFalse();
    }

    /// <summary>
    /// 认领 HTML 的引擎不可用时**报错，不改道**：同一份 HTML 在两条路径下出来的 PDF 差别极大，
    /// 悄悄改道等于让「装没装浏览器」决定合同长什么样。
    /// </summary>
    [Fact]
    public async Task ConvertToPdfAsync_ClaimingEngineIsUnavailable_TheErrorSurfaces_NoSilentFallback()
    {
        var html = new StubConverter(".html") { Available = false, Failure = "no browser" };
        var office = new StubConverter(".docx");
        var router = new RoutingDocumentConverter(html, office);

        var exception = await Should.ThrowAsync<DocumentConversionException>(
            () => router.ConvertToPdfAsync([1], "composed.html"));

        exception.Message.ShouldBe("no browser");
        office.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task ConvertToPdfAsync_NoEngineClaimsTheFormat_ThrowsAndListsTheSupportedOnes()
    {
        var router = new RoutingDocumentConverter(new StubConverter(".html"), new StubConverter(".docx"));

        var exception = await Should.ThrowAsync<DocumentConversionException>(
            () => router.ConvertToPdfAsync([1], "photo.png"));

        exception.Message.ShouldContain(".png");
        exception.Message.ShouldContain(".docx");
    }

    [Fact]
    public void Constructor_RequiresAtLeastOneEngine()
    {
        Should.Throw<ArgumentException>(() => new RoutingDocumentConverter());
    }

    private sealed class StubConverter : IDocumentConverter
    {
        private readonly string _extension;

        public StubConverter(string extension) => _extension = extension;

        public bool Available { get; init; } = true;

        public string? Failure { get; init; }

        public int Calls { get; private set; }

        public bool IsAvailable => Available;

        public bool CanConvert(string fileName)
            => fileName.EndsWith(_extension, StringComparison.OrdinalIgnoreCase);

        public Task<byte[]> ConvertToPdfAsync(byte[] source, string sourceFileName, CancellationToken ct = default)
        {
            if (Failure != null)
                throw new DocumentConversionException(Failure);

            Calls++;
            return Task.FromResult(Encoding.ASCII.GetBytes("%PDF-"));
        }
    }
}
