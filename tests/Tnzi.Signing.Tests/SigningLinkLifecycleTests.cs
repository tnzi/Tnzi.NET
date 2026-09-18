using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Security.Claims;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 签署链接的读取面也要看请求的状态：作废、拒签、过期之后，链接不再交出字段值与文档。
/// </summary>
/// <remarks>
/// <para>
/// 此前 <c>CheckSignable</c> 只挡写（提交 / 拒签），两个读端点在令牌解析得出后就把整份载荷
/// （本人全部字段值 + <c>DocumentFileId</c>）与 PDF 字节交出去，且 <c>VoidAsync</c> 不动 <c>TokenHash</c> ——
/// 一条本该过期或被叫停的链接永远保有读权限，一份发错人的请求收不回来。
/// </para>
/// <para>
/// 已完成的请求是刻意的例外：签署人拿着自己的链接取回密封成品，是文档写明的行为
/// （<c>AfterCompletion_TheTokenServesTheSealedDocument</c> 守着它）。
/// </para>
/// </remarks>
public class SigningLinkLifecycleTests : SigningStorageTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var anonymous = new Mock<ICurrentUser>();
        anonymous.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymous.SetupGet(u => u.Id).Returns((Guid?)null);
        services.AddScoped(_ => anonymous.Object);
    }

    private static List<CreateSignerDto> TwoParties() =>
    [
        new CreateSignerDto { Role = "Buyer", Name = "Alice" },
        new CreateSignerDto { Role = "Seller", Name = "Bob" },
    ];

    private static Field BuyerField(string role = "Buyer") => new()
    {
        Key = "Notes",
        Label = "Notes",
        Type = SigningFieldType.Text,
        RecipientRole = role,
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.1m,
        W = 0.5m,
        H = 0.04m,
    };

    private async Task ExpireAsync(Guid requestId)
    {
        var envelope = await DbContext.Set<Envelope>().SingleAsync(e => e.Id == requestId);
        envelope.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    // ── 作废 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Voiding_revokes_every_signer_token()
    {
        var (renderedId, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(recipients: TwoParties());
        using (var admin = BeginRequest())
        {
            (await admin.Envelopes.VoidAsync(requestId)).Succeeded.ShouldBeTrue();
        }

        (await DbContext.Set<Signer>().AsNoTracking().Where(s => s.RequestId == requestId).ToListAsync())
            .ShouldAllBe(s => s.TokenHash == null);

        foreach (var token in tokens)
        {
            using var request = BeginRequest();
            var packet = await request.Envelopes.GetByTokenAsync(token);
            packet.Succeeded.ShouldBeFalse();
            packet.Code.ShouldBe(404);

            var document = await request.Envelopes.GetDocumentByTokenAsync(token);
            document.Succeeded.ShouldBeFalse();
            document.Code.ShouldBe(404);
            request.Grants.IsGranted(renderedId).ShouldBeFalse();
        }
    }

    // ── 拒签 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_declined_request_returns_a_status_only_packet_to_the_other_party()
    {
        var (renderedId, _, tokens) = await ArrangeSentEnvelopeWithTokensAsync([BuyerField()], recipients: TwoParties());
        using (var decline = BeginRequest())
        {
            (await decline.Envelopes.DeclineAsync(tokens[1], "No thanks")).Succeeded.ShouldBeTrue();
        }

        using var request = BeginRequest();
        var packet = await request.Envelopes.GetByTokenAsync(tokens[0]);

        packet.Succeeded.ShouldBeTrue(packet.Message);
        packet.Data!.RequestStatus.ShouldBe(EnvelopeStatus.Declined);
        packet.Data.Title.ShouldBe("Engagement Letter");
        packet.Data.Fields.ShouldBeEmpty();
        packet.Data.DocumentFileId.ShouldBeNull();
        packet.Data.IsMyTurn.ShouldBeFalse();

        var document = await request.Envelopes.GetDocumentByTokenAsync(tokens[0]);
        document.Succeeded.ShouldBeFalse();
        document.Code.ShouldBe(404);
        request.Grants.IsGranted(renderedId).ShouldBeFalse();
        (await request.Files.GetAsync(renderedId)).Succeeded.ShouldBeFalse();
    }

    // ── 过期 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_expired_link_no_longer_serves_the_rendered_draft()
    {
        var (renderedId, requestId, token) = await ArrangeSentEnvelopeAsync([BuyerField("Client")]);
        await ExpireAsync(requestId);

        using var request = BeginRequest();
        var packet = await request.Envelopes.GetByTokenAsync(token);
        packet.Succeeded.ShouldBeTrue(packet.Message);
        packet.Data!.RequestStatus.ShouldBe(EnvelopeStatus.Expired);
        packet.Data.Fields.ShouldBeEmpty();
        packet.Data.DocumentFileId.ShouldBeNull();

        var document = await request.Envelopes.GetDocumentByTokenAsync(token);
        document.Succeeded.ShouldBeFalse();
        document.Code.ShouldBe(404);
        request.Grants.IsGranted(renderedId).ShouldBeFalse();
    }

    /// <summary>打开一条已经失效的链接不算「已查看」：那个时间会进完成证书，而这份请求不会完成。</summary>
    [Fact]
    public async Task Opening_an_expired_link_does_not_record_a_view()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();
        await ExpireAsync(requestId);

        using (var request = BeginRequest())
        {
            (await request.Envelopes.GetByTokenAsync(token)).Succeeded.ShouldBeTrue();
        }

        var signer = await DbContext.Set<Signer>().AsNoTracking().SingleAsync(s => s.RequestId == requestId);
        signer.ViewedAt.ShouldBeNull();
        signer.Status.ShouldBe(SigningRecipientStatus.Sent);
    }

    // ── 仍在进行中 ────────────────────────────────────────────────────────

    /// <summary>对照：一条活着的链接照常交出字段与文档（守住上面那些不是「把读端点整个关了」）。</summary>
    [Fact]
    public async Task A_live_link_still_serves_fields_and_document()
    {
        var (renderedId, _, token) = await ArrangeSentEnvelopeAsync([BuyerField("Client")]);
        using var request = BeginRequest();

        var packet = await request.Envelopes.GetByTokenAsync(token);
        packet.Succeeded.ShouldBeTrue(packet.Message);
        packet.Data!.DocumentFileId.ShouldBe(renderedId);

        (await request.Envelopes.GetDocumentByTokenAsync(token)).Succeeded.ShouldBeTrue();
        request.Grants.IsGranted(renderedId).ShouldBeTrue();
    }
}
