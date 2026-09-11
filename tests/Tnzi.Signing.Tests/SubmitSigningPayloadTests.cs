using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Security.Claims;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 匿名端点收进来的两个自由文本字段：签名图与同意条款。
/// </summary>
/// <remarks>
/// <para>
/// 它们此前是列上无上限、服务里不校验的自由文本，而写入路径不要求任何登录 ——
/// 一个匿名可写的无界字段就是存储滥用面。现在上限写在列与服务两处，取同一个常量。
/// </para>
/// <para>
/// 形态校验放在提交那一刻是为了不让一次拼错的提交钉死整份请求：图存进去之后收件人就是 Signed、
/// 不能重交，而解不出来的图会让密封在盖章那一步失败，信封停在 InProgress，没有任何人能修。
/// </para>
/// </remarks>
public class SubmitSigningPayloadTests : SigningStorageTestBase
{
    private const string TinyPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    protected override void ConfigureServices(IServiceCollection services)
    {
        var anonymous = new Mock<ICurrentUser>();
        anonymous.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymous.SetupGet(u => u.Id).Returns((Guid?)null);
        services.AddScoped(_ => anonymous.Object);
    }

    private async Task<Signer> SignerOfAsync(Guid requestId)
        => await DbContext.Set<Signer>().AsNoTracking().SingleAsync(s => s.RequestId == requestId);

    // ── 形态 ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TinyPng)]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==")]
    [InlineData("data:image/png;base64,iVBORw0KGgo\nAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==")]
    public void A_data_url_or_bare_base64_is_well_formed(string payload)
    {
        SignatureImagePayload.IsWellFormed(payload).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("data:image/png,not-base64-marker")]
    [InlineData("data:image/png;base64,")]
    [InlineData("this is not base64!!")]
    [InlineData("<svg onload=alert(1)>")]
    public void Anything_that_does_not_decode_is_rejected(string payload)
    {
        SignatureImagePayload.IsWellFormed(payload).ShouldBeFalse();
    }

    // ── 提交路径 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task An_oversized_signature_image_is_refused_before_anything_is_written()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();
        var oversized = "data:image/png;base64," + new string('A', SigningLimits.MaxSignatureImageLength);

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto { SignatureImage = oversized });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("signature image");

        var signer = await SignerOfAsync(requestId);
        signer.Status.ShouldNotBe(SigningRecipientStatus.Signed);
        signer.SignatureImage.ShouldBeNull();
    }

    [Fact]
    public async Task A_signature_image_that_cannot_be_decoded_is_refused_at_submit_not_at_seal()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto { SignatureImage = "not an image at all" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);

        // 关键在这里：请求没有被钉死。收件人仍可重交，信封仍是 Sent/Viewed 而不是一个永远密封不了的 InProgress。
        var envelope = await DbContext.Set<Envelope>().AsNoTracking().SingleAsync(e => e.Id == requestId);
        envelope.Status.ShouldNotBe(EnvelopeStatus.InProgress);
        (await SignerOfAsync(requestId)).Status.ShouldNotBe(SigningRecipientStatus.Signed);
    }

    [Fact]
    public async Task An_oversized_consent_text_is_refused()
    {
        var (_, _, token) = await ArrangeSentEnvelopeAsync();

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto
        {
            ConsentText = new string('x', SigningLimits.MaxConsentTextLength + 1),
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("consent text");
    }

    /// <summary>上限之内、形态正确的提交照常签成 —— 校验不该把正常的签名板输出挡在外面。</summary>
    [Fact]
    public async Task A_well_formed_signature_within_the_limit_still_signs()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto
        {
            SignatureImage = TinyPng,
            ConsentText = new string('x', SigningLimits.MaxConsentTextLength),
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        (await SignerOfAsync(requestId)).SignatureImage.ShouldBe(TinyPng);
    }

    /// <summary>列上限与服务校验取同一个常量 —— 两处各写一个数字就会漂。</summary>
    [Fact]
    public void The_column_limits_match_the_service_limits()
    {
        var signer = DbContext.Model.FindEntityType(typeof(Signer))!;
        signer.FindProperty(nameof(Signer.SignatureImage))!.GetMaxLength().ShouldBe(SigningLimits.MaxSignatureImageLength);
        signer.FindProperty(nameof(Signer.ConsentText))!.GetMaxLength().ShouldBe(SigningLimits.MaxConsentTextLength);
    }
}
