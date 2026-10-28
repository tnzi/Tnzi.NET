using Microsoft.EntityFrameworkCore;
using Tnzi.Documents.Models;
using Tnzi.Results;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 锚定位字段从发起到密封：多词锚要找得到，找不到的锚要在任何人签字之前被拦下。
/// </summary>
/// <remarks>
/// <para>
/// 文档读取走真实的 PdfPig（<see cref="SigningStorageTestBase.Inspector"/>），渲染稿是一份真 PDF，
/// 其中「Client」与「Signature」是两段独立的文本、中间没有空格字形，很多生成器就是这样分词的。
/// </para>
/// <para>
/// ★ 此前锚文本整段转义后去匹配（空格成了字面的 <c>"\ "</c>），这样的文档里多词锚永远找不到；
/// 密封时那个签名框被静默跳过，成品照样算哈希、出证书、归档，唯一的症状是一行 Warning。
/// 模板行在这里直接写库（不经建模板那道新加的门），模拟的正是那道门之前存下的存量模板。
/// </para>
/// </remarks>
public class EnvelopeAnchorPlacementTests : SigningStorageTestBase
{
    private const string TinyPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static readonly byte[] DocumentWithSignatureLine = TestPdfs.Pages(
        1,
        new TestPdfs.TextRun("Client", 72, 120),
        new TestPdfs.TextRun("Signature", 112, 120));

    private static Field AnchoredSignature(string anchor) => new()
    {
        Key = "clientSig",
        Label = "Client signature",
        Type = SigningFieldType.Signature,
        RecipientRole = "Client",
        Required = true,
        PlacementMode = FieldPlacementMode.Anchor,
        AnchorText = anchor,
        Page = 1,
        W = 0.3m,
        H = 0.05m,
    };

    private async Task<Result<EnvelopeDto>> CreateFromAsync(Field field)
    {
        var (templateId, _) = await ArrangeTemplateAsync([field], renderedPdf: DocumentWithSignatureLine);
        using var create = BeginRequest();
        return await create.Envelopes.CreateAsync(new CreateEnvelopeDto
        {
            TemplateId = templateId,
            Title = "Retainer",
            Recipients = SingleClient(),
        });
    }

    [Theory]
    [InlineData("Client Signature")]
    [InlineData("Signature ")]
    [InlineData("  Client   Signature")]
    public async Task A_multi_word_or_untrimmed_anchor_places_the_signature_at_sealing(string anchor)
    {
        var created = await CreateFromAsync(AnchoredSignature(anchor));
        created.Succeeded.ShouldBeTrue(created.Message);

        string token;
        using (var send = BeginRequest())
        {
            var sent = await send.Envelopes.SendAsync(created.Data!.Id);
            sent.Succeeded.ShouldBeTrue(sent.Message);
            token = sent.Data!.Single().Token;
        }

        using (var submit = BeginRequest())
        {
            var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto { SignatureImage = TinyPng });
            result.Succeeded.ShouldBeTrue(result.Message);
            result.Data!.RequestStatus.ShouldBe(EnvelopeStatus.Completed);
        }

        // 密封那一次盖章里有这张签名，而且落在锚所在的那一行（左下角原点的 y=120 → 归一化左上角原点约 0.85）。
        var seal = Stamper.Requests.Last();
        var image = seal.Stamps.OfType<PdfImageStamp>().ShouldHaveSingleItem();
        image.PageNumber.ShouldBe(1);
        image.Rect.Y.ShouldBeInRange(0.8d, 0.9d);
    }

    /// <summary>存量模板的锚在文档里不存在：发起这一步就指名拒绝，什么都不落库，而不是等所有人签完再静默留白。</summary>
    [Fact]
    public async Task Creating_a_request_whose_anchor_is_not_in_the_document_is_refused()
    {
        var result = await CreateFromAsync(AnchoredSignature("Witness Signature"));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull().ShouldContain("clientSig");
        result.Message.ShouldContain("Witness Signature");
        (await DbContext.Set<Envelope>().AsNoTracking().AnyAsync()).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Client Signature", @"Client\s*Signature")]
    [InlineData("  Client   Signature ", @"Client\s*Signature")]
    [InlineData("Sign here (Client)", @"Sign\s*here\s*\(Client\)")]
    public void The_anchor_pattern_matches_word_by_word_without_requiring_spaces(string anchor, string expected)
        => SigningSealer.AnchorPattern(anchor).ShouldBe(expected);
}
