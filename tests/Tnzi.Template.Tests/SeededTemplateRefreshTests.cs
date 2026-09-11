namespace Tnzi.Template.Tests;

/// <summary>
/// 「刷新出厂版、永不覆盖用户编辑」的判据
/// </summary>
/// <remarks>
/// ★ 本组是纯函数断言，<b>单独全绿证明不了播种器真的用上了它</b> ——
/// 走真实播种入口的在 <c>Tnzi.Finance.Tests</c> 的 <c>CheckTemplateSeederTests</c>。
/// </remarks>
public class SeededTemplateRefreshTests
{
    private const string Shipped = "<html>v2</html>";

    // ── 指纹 ─────────────────────────────────────────────────

    [Fact]
    public void Fingerprint_IgnoresNewlineStyle()
    {
        // 存储与传输环节改写换行是常事，而那不是「用户改过版式」。
        // 不归一化的话，一次 CRLF 重写就会让整行被永久判成用户所有、从此刷不动，且毫无症状。
        Assert.Equal(
            SeededTemplateRefresh.Fingerprint("a\nb\nc"),
            SeededTemplateRefresh.Fingerprint("a\r\nb\rc"));
    }

    [Fact]
    public void Fingerprint_ChangesWithContent()
        => Assert.NotEqual(SeededTemplateRefresh.Fingerprint("a"), SeededTemplateRefresh.Fingerprint("b"));

    // ── metadata 读写 ────────────────────────────────────────

    [Fact]
    public void WriteFingerprint_KeepsTheAuthorsOwnKeys()
    {
        var written = SeededTemplateRefresh.TryWriteFingerprint("""{"author":"acme","rev":3}""", "abc");

        Assert.Equal("abc", SeededTemplateRefresh.ReadFingerprint(written));
        Assert.Contains("acme", written);
        Assert.Contains("\"rev\":3", written);
    }

    [Fact]
    public void WriteFingerprint_CreatesTheObjectWhenThereIsNoMetadata()
        => Assert.Equal("abc", SeededTemplateRefresh.ReadFingerprint(SeededTemplateRefresh.TryWriteFingerprint(null, "abc")));

    [Theory]
    [InlineData("[1,2,3]")]              // JSON 但不是对象
    [InlineData("\"just a string\"")]
    [InlineData("not json at all")]
    public void WriteFingerprint_RefusesWhenTheMetadataIsNotAJsonObject(string metadata)
    {
        // 那是别人的数据结构，看不懂就一个字节都不动（调用方据此把该行当用户所有）
        Assert.Null(SeededTemplateRefresh.TryWriteFingerprint(metadata, "abc"));
        Assert.Null(SeededTemplateRefresh.ReadFingerprint(metadata));
    }

    // ── 归属判据 ─────────────────────────────────────────────

    [Fact]
    public void RowStillCarryingTheFingerprintWeWrote_IsFactoryOwned()
    {
        const string current = "<html>v1</html>";
        var metadata = SeededTemplateRefresh.TryWriteFingerprint(null, SeededTemplateRefresh.Fingerprint(current));

        // 指纹吻合 = 自我们播下去之后没人动过正文
        Assert.True(SeededTemplateRefresh.IsFactoryOwned(metadata, current, lastModificationTime: DateTime.UtcNow));
    }

    [Fact]
    public void RowWhoseBodyDivergedFromItsFingerprint_IsUserOwned()
    {
        var metadata = SeededTemplateRefresh.TryWriteFingerprint(null, SeededTemplateRefresh.Fingerprint("<html>v1</html>"));

        // 管理端把毫米坐标调过了 —— 这正是 additive 当初存在的全部理由
        Assert.False(SeededTemplateRefresh.IsFactoryOwned(metadata, "<html>v1 tweaked</html>", DateTime.UtcNow));
    }

    // ★★ 存量行（机制诞生前就播下去的，没有指纹）

    [Fact]
    public void LegacyRowNeverUpdatedSinceInsert_IsAdoptedAsFactoryOwned()
    {
        // 审计拦截器只在 Modified 时写 LastModificationTime，故 null 严格等价于
        // 「插入之后没有任何人更新过」= 仍是出厂版。
        Assert.True(SeededTemplateRefresh.IsFactoryOwned(metadata: null, "<html>v1</html>", lastModificationTime: null));
    }

    [Fact]
    public void LegacyRowThatWasEditedBeforeTheMechanismExisted_IsNeverTouched()
        => Assert.False(SeededTemplateRefresh.IsFactoryOwned(metadata: null, "<html>edited</html>", DateTime.UtcNow));

    [Fact]
    public void OnceAdopted_TheFingerprintDecides_NotTheModificationTime()
    {
        // ★ 采纳那一次写入本身会把 LastModificationTime 置上。若判据还看它，
        // 每一行都会在被采纳的下一刻变成「用户所有」，从此再也刷不动 ——
        // 也就是说这个机制只会生效一次。
        var metadata = SeededTemplateRefresh.TryWriteFingerprint(null, SeededTemplateRefresh.Fingerprint(Shipped));

        Assert.True(SeededTemplateRefresh.IsFactoryOwned(metadata, Shipped, lastModificationTime: DateTime.UtcNow));
    }

    [Fact]
    public void MetadataThatIsNotAJsonObject_ReadsAsNoFingerprint()
    {
        // 读不懂就退回存量行判据，而不是崩掉
        Assert.True(SeededTemplateRefresh.IsFactoryOwned("not json", "<html>v1</html>", lastModificationTime: null));
        Assert.False(SeededTemplateRefresh.IsFactoryOwned("not json", "<html>v1</html>", DateTime.UtcNow));
    }
}
