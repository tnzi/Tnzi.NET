using Microsoft.EntityFrameworkCore;
using Mapster;
using MapsterMapper;
using Tnzi.Mapster;
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

    /// <summary>
    /// 单收件人的请求从不经过 InProgress：最后（也是唯一）一次提交直接走向密封。密封在抢占之后<b>抛</b>出来
    /// （存储超时）而不是返回失败时，它此前停在 Sent + CompletedAt：收件人重交答「已签」、
    /// 管理端重新密封答 409，只剩作废重签。现在退回 InProgress、没有完成时刻，重新密封能把它做完。
    /// </summary>
    [Fact]
    public async Task A_single_recipient_seal_that_throws_is_left_resealable()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();

        Storage.FailNextDownload = true;
        using (var submit = BeginRequest())
        {
            try
            {
                await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto());
            }
            catch (TimeoutException)
            {
                // 旧行为：异常一路出栈。状态断言在下面。
            }
        }

        var stuck = await EnvelopeAsync(requestId);
        stuck.Status.ShouldBe(EnvelopeStatus.InProgress);
        stuck.CompletedAt.ShouldBeNull();
        stuck.FinalPdfFileId.ShouldBeNull();

        using var reseal = BeginRequest();
        var result = await reseal.Envelopes.SealAsync(requestId);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Status.ShouldBe(EnvelopeStatus.Completed);
    }

    /// <summary>
    /// 收件人点完「签署」就断开：请求的取消令牌在密封途中被取消。签名已经落库、不能重交，
    /// 密封必须照样做完，而不是把请求留在半路上。
    /// </summary>
    [Fact]
    public async Task A_client_disconnect_during_sealing_does_not_strand_the_envelope()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();

        using var disconnect = new CancellationTokenSource();
        Storage.OnNextDownload = disconnect.Cancel;
        using (var submit = BeginRequest())
        {
            try
            {
                await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto(), disconnect.Token);
            }
            catch (OperationCanceledException)
            {
                // 断开之后响应本来就送不到，抛不抛无所谓；要紧的是库里的状态。
            }
        }

        var after = await EnvelopeAsync(requestId);
        after.Status.ShouldBe(EnvelopeStatus.Completed);
        after.FinalPdfFileId.ShouldNotBeNull();
        after.Sha256.ShouldNotBeNullOrWhiteSpace();
        after.CompletedAt.ShouldNotBeNull();
    }

    /// <summary>管理端重新密封同理：抢占落库之后调用方断开，密封照样做完。</summary>
    [Fact]
    public async Task A_disconnect_during_an_admin_reseal_does_not_strand_the_envelope()
    {
        var (requestId, _) = await ArrangeStuckEnvelopeAsync();

        using var disconnect = new CancellationTokenSource();
        Storage.OnNextDownload = disconnect.Cancel;
        using (var reseal = BeginRequest())
        {
            try
            {
                await reseal.Envelopes.SealAsync(requestId, disconnect.Token);
            }
            catch (OperationCanceledException)
            {
                // 响应送不到无所谓；要紧的是库里的状态。
            }
        }

        var after = await EnvelopeAsync(requestId);
        after.Status.ShouldBe(EnvelopeStatus.Completed);
        after.FinalPdfFileId.ShouldNotBeNull();
        after.CompletedAt.ShouldNotBeNull();
    }

    /// <summary>旧版本留下的 Sent + 全员 Signed（+ 完成时刻）也要能由管理端密封，不然这些行只能作废。</summary>
    [Fact]
    public async Task A_stranded_sent_envelope_whose_recipients_all_signed_can_be_sealed()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();
        Stamper.FailNext = true;
        using (var submit = BeginRequest())
        {
            (await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto())).Succeeded.ShouldBeTrue();
        }

        // 把它摆回旧版本留下的形态。
        var row = await DbContext.Set<Envelope>().SingleAsync(e => e.Id == requestId);
        row.Status = EnvelopeStatus.Sent;
        row.CompletedAt = DateTime.UtcNow.AddMinutes(-5);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using var reseal = BeginRequest();
        var result = await reseal.Envelopes.SealAsync(requestId);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Status.ShouldBe(EnvelopeStatus.Completed);
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

    /// <summary>
    /// 已经密封过的不再密封：哈希只算一次，重算一遍就让它失去意义。对已完成的请求再调一次只补做密封之后的步骤
    /// （见 <see cref="EnvelopePostSealRecoveryTests"/>），不会再盖一次章。
    /// </summary>
    [Fact]
    public async Task Sealing_a_completed_envelope_again_never_reseals_it()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();
        using (var submit = BeginRequest())
        {
            (await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto())).Succeeded.ShouldBeTrue();
        }
        var before = await EnvelopeAsync(requestId);
        var stamps = Stamper.Requests.Count;

        using var reseal = BeginRequest();
        var result = await reseal.Envelopes.SealAsync(requestId);

        result.Succeeded.ShouldBeTrue(result.Message);
        Stamper.Requests.Count.ShouldBe(stamps);
        var after = await EnvelopeAsync(requestId);
        after.FinalPdfFileId.ShouldBe(before.FinalPdfFileId);
        after.Sha256.ShouldBe(before.Sha256);
        after.CompletedAt.ShouldBe(before.CompletedAt);
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

    /// <summary>
    /// 签名按时收齐、密封失败、之后才过截止时间：它不是「过期」，它不再等任何人签。
    /// 此前它被算成 Expired：管理端的重新密封与作废两个动作（都按派生状态显示）一起消失，收件人的链接也取不到文档，
    /// 而后端按库里的状态其实都接受，只能靠手工调接口救回来。
    /// </summary>
    [Fact]
    public async Task A_stuck_envelope_past_its_deadline_still_reads_as_in_progress_and_can_be_resealed()
    {
        using var mapping = MapperExtensions.PushMapper(new Mapper(new TypeAdapterConfig()));

        var (requestId, token) = await ArrangeStuckEnvelopeAsync();

        var stuck = await DbContext.Set<Envelope>().SingleAsync(e => e.Id == requestId);
        stuck.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using (var admin = BeginRequest())
        {
            (await admin.Envelopes.GetAsync(requestId)).Data!.Status.ShouldBe(EnvelopeStatus.InProgress);

            var inProgress = await admin.Envelopes.GetPagedAsync(new EnvelopeQueryDto { Status = EnvelopeStatus.InProgress });
            inProgress.Data!.Items.ShouldContain(i => i.Id == requestId && i.Status == EnvelopeStatus.InProgress);

            var expired = await admin.Envelopes.GetPagedAsync(new EnvelopeQueryDto { Status = EnvelopeStatus.Expired });
            expired.Data!.Items.ShouldNotContain(i => i.Id == requestId);

            var all = await admin.Envelopes.GetPagedAsync(new EnvelopeQueryDto());
            all.Data!.Items.Single(i => i.Id == requestId).Status.ShouldBe(EnvelopeStatus.InProgress);
        }

        using (var read = BeginRequest())
        {
            var packet = await read.Envelopes.GetByTokenAsync(token);
            packet.Data!.RequestStatus.ShouldBe(EnvelopeStatus.InProgress);
            (await read.Envelopes.GetDocumentByTokenAsync(token)).Succeeded.ShouldBeTrue();
        }

        using var reseal = BeginRequest();
        var result = await reseal.Envelopes.SealAsync(requestId);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Status.ShouldBe(EnvelopeStatus.Completed);
    }

    /// <summary>反方向：还有人没签的请求过了截止时间照常是 Expired（筛选与派生同一口径）。</summary>
    [Fact]
    public async Task An_envelope_still_waiting_for_a_signature_expires_as_before()
    {
        using var mapping = MapperExtensions.PushMapper(new Mapper(new TypeAdapterConfig()));

        var (_, requestId, _) = await ArrangeSentEnvelopeAsync();

        var sent = await DbContext.Set<Envelope>().SingleAsync(e => e.Id == requestId);
        sent.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using var admin = BeginRequest();
        (await admin.Envelopes.GetAsync(requestId)).Data!.Status.ShouldBe(EnvelopeStatus.Expired);
        var expired = await admin.Envelopes.GetPagedAsync(new EnvelopeQueryDto { Status = EnvelopeStatus.Expired });
        expired.Data!.Items.ShouldContain(i => i.Id == requestId && i.Status == EnvelopeStatus.Expired);
    }
}
