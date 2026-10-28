using Microsoft.EntityFrameworkCore;
using Tnzi.Documents.Models;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 密封之后的两步（完成证书、交宿主归档）失败了怎么补。
/// </summary>
/// <remarks>
/// <para>
/// 两步在密封那次提交里失败都只记日志、不回退密封，这是对的，文档已经签成。可契约同时承诺了「会重试」：
/// <c>IDocumentHostSink</c> 要求幂等，理由是「完成动作会在瞬时失败后被重试」；证书「之后可以补生成」。
/// 而此前唯一的恢复入口（管理端 seal）对已完成的请求一律 409，于是一次归档超时 = 签好的合同永远不进案卷，
/// 一次证书失败 = 证据链永远缺一页，留下的只有一行错误日志。
/// </para>
/// <para>
/// 现在管理端对一份已完成的请求再调一次 seal，补做这两步：证书缺失才生成，sink 重新调用；成品与哈希一个字节都不动。
/// </para>
/// </remarks>
public class EnvelopePostSealRecoveryTests : SigningStorageTestBase
{
    private const string HostType = "TestMatter";

    private readonly FlakySink _sink = new();

    protected override IEnumerable<IDocumentHostSink> HostSinks => [_sink];

    /// <summary>契约要求 sink 与 provider 成对注册（provider 是绑定的授权点），这里放行每一条记录。</summary>
    protected override IEnumerable<IMergeSourceProvider> MergeProviders => [new AllowAllSource()];

    private sealed class AllowAllSource : IMergeSourceProvider
    {
        public string EntityType => HostType;

        public Task<IReadOnlyDictionary<string, object?>> ResolveAsync(Guid entityId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
    }

    /// <summary>可以让下一次归档抛异常（模拟一次超时）的 sink；记下每一次成功的归档。</summary>
    private sealed class FlakySink : IDocumentHostSink
    {
        public string EntityType => HostType;

        /// <summary>置位后接下来这么多次归档抛异常。</summary>
        public int FailNext { get; set; }

        public int Calls { get; private set; }

        public List<(Guid EntityId, Guid FileId, string FileName)> Attached { get; } = [];

        public Task AttachAsync(Guid entityId, Guid fileId, string fileName, Guid requestId, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (FailNext > 0)
            {
                FailNext--;
                throw new TimeoutException("Simulated document-management timeout.");
            }

            Attached.Add((entityId, fileId, fileName));
            return Task.CompletedTask;
        }
    }

    private async Task<Envelope> EnvelopeAsync(Guid requestId)
        => await DbContext.Set<Envelope>().AsNoTracking().SingleAsync(e => e.Id == requestId);

    /// <summary>绑定宿主的单收件人请求，签完（于是密封）。返回请求 id 与宿主 id。</summary>
    private async Task<(Guid RequestId, Guid HostId)> ArrangeSignedEnvelopeAsync()
    {
        var hostId = Guid.NewGuid();
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(hostEntityType: HostType, hostEntityId: hostId);
        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(tokens.Single(), new SubmitSigningDto());
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.RequestStatus.ShouldBe(EnvelopeStatus.Completed);
        return (requestId, hostId);
    }

    private int SealedDocumentCount() => Stamper.Requests.Count;

    [Fact]
    public async Task An_archive_that_failed_at_sealing_is_attached_by_sealing_the_completed_request_again()
    {
        _sink.FailNext = 1;
        var (requestId, hostId) = await ArrangeSignedEnvelopeAsync();

        _sink.Attached.ShouldBeEmpty();
        var before = await EnvelopeAsync(requestId);
        before.Status.ShouldBe(EnvelopeStatus.Completed);
        var stampsBefore = SealedDocumentCount();

        using (var admin = BeginRequest())
        {
            var result = await admin.Envelopes.SealAsync(requestId);
            result.Succeeded.ShouldBeTrue(result.Message);
            result.Data!.Status.ShouldBe(EnvelopeStatus.Completed);
        }

        // 归档补上了，挂的是同一份成品。
        _sink.Attached.Count.ShouldBe(1);
        _sink.Attached[0].EntityId.ShouldBe(hostId);
        _sink.Attached[0].FileId.ShouldBe(before.FinalPdfFileId!.Value);
        _sink.Attached[0].FileName.ShouldEndWith("-signed.pdf");

        // ★ 成品与哈希一个字节都不动：没有第二次盖章，没有新的成品。
        SealedDocumentCount().ShouldBe(stampsBefore);
        var after = await EnvelopeAsync(requestId);
        after.FinalPdfFileId.ShouldBe(before.FinalPdfFileId);
        after.Sha256.ShouldBe(before.Sha256);
        after.CompletedAt.ShouldBe(before.CompletedAt);
        after.CompletionCertificateFileId.ShouldBe(before.CompletionCertificateFileId);
    }

    [Fact]
    public async Task A_certificate_that_failed_at_sealing_is_produced_by_sealing_the_completed_request_again()
    {
        Stamper.FailNextCreate = true;
        var (requestId, _) = await ArrangeSignedEnvelopeAsync();

        var before = await EnvelopeAsync(requestId);
        before.Status.ShouldBe(EnvelopeStatus.Completed);
        before.CompletionCertificateFileId.ShouldBeNull();

        using (var admin = BeginRequest())
        {
            var result = await admin.Envelopes.SealAsync(requestId);
            result.Succeeded.ShouldBeTrue(result.Message);
            result.Data!.CompletionCertificateFileId.ShouldNotBeNull();
        }

        var after = await EnvelopeAsync(requestId);
        after.CompletionCertificateFileId.ShouldNotBeNull();
        after.FinalPdfFileId.ShouldBe(before.FinalPdfFileId);
        after.Sha256.ShouldBe(before.Sha256);

        // 补出来的证书写的是密封那一刻算定的同一个哈希。
        var certificate = Stamper.Creates.Last();
        certificate.Stamps.OfType<PdfTextStamp>().ShouldContain(s => s.Text == before.Sha256);
    }

    /// <summary>证书已经在的请求不再生成第二份：一份请求只该有一张完成证书。</summary>
    [Fact]
    public async Task Sealing_a_completed_request_again_does_not_issue_a_second_certificate()
    {
        var (requestId, _) = await ArrangeSignedEnvelopeAsync();
        var before = await EnvelopeAsync(requestId);
        before.CompletionCertificateFileId.ShouldNotBeNull();
        var certificates = Stamper.Creates.Count;

        using (var admin = BeginRequest())
        {
            (await admin.Envelopes.SealAsync(requestId)).Succeeded.ShouldBeTrue();
        }

        Stamper.Creates.Count.ShouldBe(certificates);
        (await EnvelopeAsync(requestId)).CompletionCertificateFileId.ShouldBe(before.CompletionCertificateFileId);
    }

    /// <summary>补做仍然失败就如实说失败（500，指出是哪一步）；请求保持已完成，成品原样。</summary>
    [Fact]
    public async Task A_recovery_whose_archive_fails_again_says_so_and_leaves_the_seal_alone()
    {
        _sink.FailNext = 2;
        var (requestId, _) = await ArrangeSignedEnvelopeAsync();
        var before = await EnvelopeAsync(requestId);

        using (var admin = BeginRequest())
        {
            var result = await admin.Envelopes.SealAsync(requestId);
            result.Succeeded.ShouldBeFalse();
            result.Code.ShouldBe(500);
            result.Message.ShouldNotBeNull().ShouldContain("host record");
        }

        _sink.Calls.ShouldBe(2);
        var after = await EnvelopeAsync(requestId);
        after.Status.ShouldBe(EnvelopeStatus.Completed);
        after.Sha256.ShouldBe(before.Sha256);
        after.FinalPdfFileId.ShouldBe(before.FinalPdfFileId);

        // 再来一次就补上了。
        using var retry = BeginRequest();
        (await retry.Envelopes.SealAsync(requestId)).Succeeded.ShouldBeTrue();
        _sink.Attached.Count.ShouldBe(1);
    }

    /// <summary>不绑定宿主的独立文档：没有归档可补，再调一次 seal 也只是把已完成的请求原样交回。</summary>
    [Fact]
    public async Task Sealing_a_completed_standalone_request_again_changes_nothing()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync();
        using (var submit = BeginRequest())
        {
            (await submit.Envelopes.SubmitAsync(tokens.Single(), new SubmitSigningDto())).Succeeded.ShouldBeTrue();
        }

        var before = await EnvelopeAsync(requestId);

        using var admin = BeginRequest();
        var result = await admin.Envelopes.SealAsync(requestId);

        result.Succeeded.ShouldBeTrue(result.Message);
        _sink.Calls.ShouldBe(0);
        var after = await EnvelopeAsync(requestId);
        after.Sha256.ShouldBe(before.Sha256);
        after.FinalPdfFileId.ShouldBe(before.FinalPdfFileId);
        after.CompletionCertificateFileId.ShouldBe(before.CompletionCertificateFileId);
    }
}
