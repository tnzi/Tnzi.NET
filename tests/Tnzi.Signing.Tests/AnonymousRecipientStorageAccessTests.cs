using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Cryptography;
using Tnzi.Security.Claims;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 匿名收件人 + <b>真实的</b> Storage 栈。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么要有这一组。</b>本模块此前的 40 条测试全部 <c>Mock&lt;IFileStorageService&gt;</c>，
/// 真实的读取判定一次都没跑过。而本模块唯一出货的签署入口是 <c>[AllowAnonymous]</c>：
/// 密封在 <c>SigningSealer</c> 里 <c>GetAsync(渲染稿)</c>，那条路对匿名一律 404 ——
/// 于是最后一位签署人提交后密封必失败，信封永远停在 <c>InProgress</c>，收件人侧显示已签、
/// 管理端看到的却是一份永不完成的请求，没有成品、没有哈希、没有证书，只有一行 LogError。
/// 同根的另一面：收件人拿到 <c>DocumentFileId</c>，却没有任何一条路径能取到字节。
/// </para>
/// <para>
/// 修法与分享链接同形：令牌校验通过后把这份请求的文档写进请求级授予
/// （<c>IFileAccessGrantContext</c>，判据 4），只给读、只在这一次请求内、
/// 只覆盖这份请求自己的文档。这一组既钉住「能」（密封成功 / 收件人取得到），
/// 也钉住「只能」（别的文件不放行 / 假令牌一个都不放行）。
/// </para>
/// </remarks>
public class AnonymousRecipientStorageAccessTests : SigningStorageTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        // 后注册者胜出：把基类那个「已登录」的当前用户换成匿名访客 —— 收件人就是匿名的，
        // 拿已登录用户测这条路，测的是另一件事。
        var anonymous = new Mock<ICurrentUser>();
        anonymous.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymous.SetupGet(u => u.Id).Returns((Guid?)null);
        services.AddScoped(_ => anonymous.Object);
    }

    // ── 能 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 最后一位签署人匿名提交 → 密封成功。修复之前这里停在 InProgress：
    /// SigningSealer 读渲染稿撞上 Storage 的匿名 404，而全部既有测试都 mock 掉了存储。
    /// </summary>
    [Fact]
    public async Task TheLastSigner_SubmitsAnonymously_AndTheEnvelopeIsSealed()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.RequestStatus.ShouldBe(EnvelopeStatus.Completed);

        var envelope = await DbContext.Set<Envelope>().AsNoTracking().FirstAsync(e => e.Id == requestId);
        envelope.Status.ShouldBe(EnvelopeStatus.Completed);
        envelope.FinalPdfFileId.ShouldNotBeNull();
        envelope.CompletionCertificateFileId.ShouldNotBeNull();

        // 记在案的哈希就是成品字节的哈希 —— 成品确实被存下来并且读得回来。
        using var verify = BeginRequest();
        var sealedBytes = await ReadAllAsync((await verify.Envelopes.GetDocumentByTokenAsync(token)).Data!.Content);
        envelope.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(sealedBytes)));
    }

    /// <summary>收件人凭令牌取回自己正在签的那份文档 —— 匿名，不带任何别的凭据。</summary>
    [Fact]
    public async Task TheRecipient_CanReadTheDocumentTheyAreSigning_ThroughTheToken()
    {
        var (_, _, token) = await ArrangeSentEnvelopeAsync();

        using var read = BeginRequest();
        var document = await read.Envelopes.GetDocumentByTokenAsync(token);

        document.Succeeded.ShouldBeTrue(document.Message);
        document.Data!.ContentType.ShouldBe("application/pdf");
        document.Data.FileName.ShouldBe("contract.pdf");
        (await ReadAllAsync(document.Data.Content)).ShouldBe(RenderedPdf);
    }

    /// <summary>签完之后同一个令牌给的是密封成品，不再是渲染稿。</summary>
    [Fact]
    public async Task AfterCompletion_TheTokenServesTheSealedDocument()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();
        using (var submit = BeginRequest())
        {
            (await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto())).Succeeded.ShouldBeTrue();
        }

        using var read = BeginRequest();
        var document = await read.Envelopes.GetDocumentByTokenAsync(token);
        var bytes = await ReadAllAsync(document.Data!.Content);

        var envelope = await DbContext.Set<Envelope>().AsNoTracking().FirstAsync(e => e.Id == requestId);
        bytes.ShouldNotBe(RenderedPdf);
        Convert.ToHexStringLower(SHA256.HashData(bytes)).ShouldBe(envelope.Sha256);
    }

    // ── 只能 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 授予的范围是<b>这份请求自己的文档</b>，不是「匿名调用方能读全部文件」。
    /// 修复若写成放宽守卫，这条会红。
    /// </summary>
    [Fact]
    public async Task TheToken_GrantsOnlyThisEnvelopesDocuments()
    {
        var (renderedId, _, token) = await ArrangeSentEnvelopeAsync();
        var unrelatedId = await StoreAsync("someone-elses.pdf", [0x01, 0x02, 0x03]);

        using var request = BeginRequest();
        (await request.Envelopes.GetByTokenAsync(token)).Succeeded.ShouldBeTrue();

        request.Grants.IsGranted(renderedId).ShouldBeTrue();
        request.Grants.IsGranted(unrelatedId).ShouldBeFalse();

        (await request.Files.GetAsync(renderedId)).Succeeded.ShouldBeTrue();
        var unrelated = await request.Files.GetAsync(unrelatedId);
        unrelated.Succeeded.ShouldBeFalse();
        unrelated.Code.ShouldBe(404);
    }

    /// <summary>一个对不上任何收件人的令牌什么都不放行，文档路径与文件路径同答 404。</summary>
    [Fact]
    public async Task AnUnknownToken_GrantsNothing()
    {
        var (renderedId, _, _) = await ArrangeSentEnvelopeAsync();

        using var request = BeginRequest();
        var document = await request.Envelopes.GetDocumentByTokenAsync("not-a-token");

        document.Succeeded.ShouldBeFalse();
        document.Code.ShouldBe(404);
        request.Grants.IsGranted(renderedId).ShouldBeFalse();
        (await request.Files.GetAsync(renderedId)).Succeeded.ShouldBeFalse();
    }
}
