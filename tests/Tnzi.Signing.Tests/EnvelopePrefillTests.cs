using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Documents.Models;
using Tnzi.Security.Claims;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 发出时把发起方负责的字段值（合并变量 / 预填）烧进一份本信封自有的渲染稿：签署人看到的就是将被密封的内容。
/// </summary>
/// <remarks>
/// <para>
/// 此前 Uploaded 模板的信封发的是模板渲染稿的原字节，绑定字段的值只在密封那一刻才盖上；而收件人载荷只含
/// 本人角色的字段，发起方字段不在其中 —— 签署人在签的时候**结构性地看不到**价格、地址这些将出现在成品上的内容，
/// 成品的哈希与证书却据此生成。三处 XML 文档（<c>Envelope.RenderedPdfFileId</c> / <c>SendAsync</c> / <c>Field.Binding</c>）
/// 都写着「发出时已烧进去」，那从来没有实现过。
/// </para>
/// <para>
/// 现在 <c>SendAsync</c> 用 <c>IPdfStamper</c> 把有值的发起方字段盖到一份新文件上、写回 <c>Envelope.RenderedPdfFileId</c>，
/// 快照记下烧进去的键，密封时跳过它们（否则双重盖章）；收件人载荷另带一份只读的 <c>PrefilledFields</c>。
/// Composed 模板同样处理：正文 <c>{{var}}</c> 早在排版时就在纸面上，但 <c>[[field]]</c> 绑定字段与 Uploaded 是同一套机制。
/// </para>
/// </remarks>
public class EnvelopePrefillTests : SigningStorageTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var anonymous = new Mock<ICurrentUser>();
        anonymous.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymous.SetupGet(u => u.Id).Returns((Guid?)null);
        services.AddScoped(_ => anonymous.Object);
    }

    private static Field SenderField(string key = "Price", string label = "Price") => new()
    {
        Key = key,
        Label = label,
        Type = SigningFieldType.Text,
        RecipientRole = null,
        Binding = "Amount",
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.2m,
        W = 0.3m,
        H = 0.04m,
    };

    private static Field ClientField() => new()
    {
        Key = "Notes",
        Label = "Notes",
        Type = SigningFieldType.Text,
        RecipientRole = "Client",
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.3m,
        W = 0.3m,
        H = 0.04m,
    };

    private static IEnumerable<string> TextsOf(PdfStampRequest request)
        => request.Stamps.OfType<PdfTextStamp>().Select(s => s.Text);

    [Fact]
    public async Task An_uploaded_envelope_carries_its_bound_values_before_anyone_signs()
    {
        var (templateRenderedId, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(
            [SenderField()],
            prefilledValues: new Dictionary<string, string?> { ["Price"] = "100.00" });

        // 信封有了自己的渲染稿，不再是模板的那份。
        var envelope = await DbContext.Set<Envelope>().AsNoTracking().SingleAsync(e => e.Id == requestId);
        envelope.RenderedPdfFileId.ShouldNotBeNull();
        envelope.RenderedPdfFileId.ShouldNotBe(templateRenderedId);

        // 发出那一次盖章盖的是发起方的值。
        Stamper.Requests.Count.ShouldBe(1);
        TextsOf(Stamper.Requests[0]).ShouldBe(["100.00"]);

        // 签署人凭令牌拿到的是这份烧好值的稿子，不是模板原字节。
        using var read = BeginRequest();
        var document = await read.Envelopes.GetDocumentByTokenAsync(tokens.Single());
        document.Succeeded.ShouldBeTrue(document.Message);
        (await ReadAllAsync(document.Data!.Content)).ShouldNotBe(RenderedPdf);
    }

    [Fact]
    public async Task Recipients_see_the_prefilled_values_read_only()
    {
        var (_, _, tokens) = await ArrangeSentEnvelopeWithTokensAsync(
            [SenderField(), ClientField()],
            prefilledValues: new Dictionary<string, string?> { ["Price"] = "100.00" });

        using var read = BeginRequest();
        var packet = await read.Envelopes.GetByTokenAsync(tokens.Single());

        packet.Succeeded.ShouldBeTrue(packet.Message);
        packet.Data!.Fields.Select(f => f.Key).ShouldBe(["Notes"]);
        packet.Data.PrefilledFields.Select(f => (f.Key, f.Value)).ShouldBe([("Price", "100.00")]);
    }

    [Fact]
    public async Task Sealing_does_not_stamp_a_prefilled_value_twice()
    {
        var (_, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(
            [SenderField(), ClientField()],
            prefilledValues: new Dictionary<string, string?> { ["Price"] = "100.00" });

        using (var submit = BeginRequest())
        {
            var result = await submit.Envelopes.SubmitAsync(tokens.Single(), new SubmitSigningDto
            {
                Values = new Dictionary<string, string?> { ["Notes"] = "Agreed" },
            });
            result.Succeeded.ShouldBeTrue(result.Message);
            result.Data!.RequestStatus.ShouldBe(EnvelopeStatus.Completed);
        }

        // 两次盖章：发出前一次（Price），密封一次（只有 Notes）。
        Stamper.Requests.Count.ShouldBe(2);
        TextsOf(Stamper.Requests[0]).ShouldBe(["100.00"]);
        TextsOf(Stamper.Requests[1]).ShouldBe(["Agreed"]);

        (await DbContext.Set<Envelope>().AsNoTracking().SingleAsync(e => e.Id == requestId)).Status.ShouldBe(EnvelopeStatus.Completed);
    }

    /// <summary>没有任何发起方值时不生成多余的文件：信封照旧引用模板的渲染稿。</summary>
    [Fact]
    public async Task Send_keeps_the_template_file_when_there_is_nothing_to_prefill()
    {
        var (templateRenderedId, requestId, _) = await ArrangeSentEnvelopeWithTokensAsync([ClientField()]);

        var envelope = await DbContext.Set<Envelope>().AsNoTracking().SingleAsync(e => e.Id == requestId);
        envelope.RenderedPdfFileId.ShouldBe(templateRenderedId);
        Stamper.Requests.ShouldBeEmpty();
    }

    /// <summary>预填的盖章失败就不发：发一份「文档里没有价格、成品上有」的信封正是要修的那件事。</summary>
    [Fact]
    public async Task Send_refuses_when_the_prefill_cannot_be_rendered()
    {
        var (templateId, _) = await ArrangeTemplateAsync([SenderField()]);
        Guid requestId;
        using (var create = BeginRequest())
        {
            var created = await create.Envelopes.CreateAsync(new CreateEnvelopeDto
            {
                TemplateId = templateId,
                Title = "Engagement Letter",
                Recipients = SingleClient(),
                PrefilledValues = new Dictionary<string, string?> { ["Price"] = "100.00" },
            });
            created.Succeeded.ShouldBeTrue(created.Message);
            requestId = created.Data!.Id;
        }

        Stamper.FailNext = true;
        using var send = BeginRequest();
        var sent = await send.Envelopes.SendAsync(requestId);

        sent.Succeeded.ShouldBeFalse();
        sent.Code.ShouldBe(500);
        var envelope = await DbContext.Set<Envelope>().AsNoTracking().SingleAsync(e => e.Id == requestId);
        envelope.Status.ShouldBe(EnvelopeStatus.Draft);
        (await DbContext.Set<Signer>().AsNoTracking().SingleAsync(s => s.RequestId == requestId)).TokenHash.ShouldBeNull();
    }
}
