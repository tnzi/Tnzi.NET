using Microsoft.EntityFrameworkCore;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 管理端重新密封：最后一位签完而密封失败的请求，此前没有任何人能修。
/// </summary>
/// <remarks>
/// <para>
/// 密封在最后一位收件人的那次提交里进行；盖章或存文件一旦失败，请求退回 <c>InProgress</c>，
/// 收件人已全部 <c>Signed</c> 不能重交，<c>IEnvelopeService</c> 此前也没有任何一条路径再触发密封 ——
/// 每一个已收集的签名都作废，作废重发是唯一出口。存储的一次瞬时故障就足以造成这个局面。
/// </para>
/// <para>
/// 这一组以真实的 Storage 栈跑完整条路径：模拟一次盖章失败，再由管理端重新密封。
/// </para>
/// </remarks>
public class EnvelopeResealTests : SigningStorageTestBase
{
    private async Task<Envelope> EnvelopeAsync(Guid requestId)
        => await DbContext.Set<Envelope>().AsNoTracking().SingleAsync(e => e.Id == requestId);

    /// <summary>签完但密封失败：请求停在 InProgress，收件人是 Signed。</summary>
    private async Task<(Guid RequestId, string Token)> ArrangeStuckEnvelopeAsync()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();

        Stamper.FailNext = true;
        using (var submit = BeginRequest())
        {
            var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto());
            result.Succeeded.ShouldBeTrue(result.Message);
            result.Data!.RequestStatus.ShouldBe(EnvelopeStatus.InProgress);
        }

        var stuck = await EnvelopeAsync(requestId);
        stuck.Status.ShouldBe(EnvelopeStatus.InProgress);
        stuck.FinalPdfFileId.ShouldBeNull();
        stuck.CompletedAt.ShouldBeNull();
        return (requestId, token);
    }

    [Fact]
    public async Task Resealing_an_envelope_whose_seal_failed_completes_it()
    {
        var (requestId, token) = await ArrangeStuckEnvelopeAsync();

        using (var reseal = BeginRequest())
        {
            var result = await reseal.Envelopes.SealAsync(requestId);
            result.Succeeded.ShouldBeTrue(result.Message);
            result.Data!.Status.ShouldBe(EnvelopeStatus.Completed);
            result.Data.FinalPdfFileId.ShouldNotBeNull();
            result.Data.Sha256.ShouldNotBeNullOrWhiteSpace();
            result.Data.CompletionCertificateFileId.ShouldNotBeNull();
        }

        var sealed_ = await EnvelopeAsync(requestId);
        sealed_.Status.ShouldBe(EnvelopeStatus.Completed);
        sealed_.CompletedAt.ShouldNotBeNull();

        // 收件人凭同一个令牌拿到的是密封成品。
        using var read = BeginRequest();
        var document = await read.Envelopes.GetDocumentByTokenAsync(token);
        document.Succeeded.ShouldBeTrue(document.Message);
        (await ReadAllAsync(document.Data!.Content)).ShouldNotBe(RenderedPdf);
    }

    [Fact]
    public async Task Resealing_refuses_while_someone_has_not_signed()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(
            recipients: [new CreateSignerDto { Role = "Buyer", Name = "Alice" }, new CreateSignerDto { Role = "Seller", Name = "Bob" }]);
        using (var submit = BeginRequest())
        {
            (await submit.Envelopes.SubmitAsync(tokens[0], new SubmitSigningDto())).Succeeded.ShouldBeTrue();
        }

        using var reseal = BeginRequest();
        var result = await reseal.Envelopes.SealAsync(requestId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        (await EnvelopeAsync(requestId)).Status.ShouldBe(EnvelopeStatus.InProgress);
    }

    /// <summary>已经密封过的不再密封：哈希只算一次，重算一遍就让它失去意义。</summary>
    [Fact]
    public async Task Resealing_a_completed_envelope_is_refused()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();
        using (var submit = BeginRequest())
        {
            (await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto())).Succeeded.ShouldBeTrue();
        }
        var before = await EnvelopeAsync(requestId);

        using var reseal = BeginRequest();
        var result = await reseal.Envelopes.SealAsync(requestId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        var after = await EnvelopeAsync(requestId);
        after.FinalPdfFileId.ShouldBe(before.FinalPdfFileId);
        after.Sha256.ShouldBe(before.Sha256);
    }

    [Fact]
    public async Task Resealing_a_voided_envelope_is_refused()
    {
        var (requestId, _) = await ArrangeStuckEnvelopeAsync();
        using (var admin = BeginRequest())
        {
            (await admin.Envelopes.VoidAsync(requestId)).Succeeded.ShouldBeTrue();
        }

        using var reseal = BeginRequest();
        var result = await reseal.Envelopes.SealAsync(requestId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        (await EnvelopeAsync(requestId)).Status.ShouldBe(EnvelopeStatus.Voided);
    }

    [Fact]
    public async Task Resealing_an_unknown_envelope_is_404()
    {
        using var reseal = BeginRequest();
        var result = await reseal.Envelopes.SealAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    /// <summary>第二次失败仍然不谎报：状态退回 InProgress，可以再试。</summary>
    [Fact]
    public async Task A_reseal_that_fails_again_leaves_the_envelope_retryable()
    {
        var (requestId, _) = await ArrangeStuckEnvelopeAsync();

        Stamper.FailNext = true;
        using (var reseal = BeginRequest())
        {
            var result = await reseal.Envelopes.SealAsync(requestId);
            result.Succeeded.ShouldBeFalse();
            result.Code.ShouldBe(500);
        }

        var stuck = await EnvelopeAsync(requestId);
        stuck.Status.ShouldBe(EnvelopeStatus.InProgress);
        stuck.CompletedAt.ShouldBeNull();

        using var retry = BeginRequest();
        (await retry.Envelopes.SealAsync(requestId)).Succeeded.ShouldBeTrue();
    }
}
