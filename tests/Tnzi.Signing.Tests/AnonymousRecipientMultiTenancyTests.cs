using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Data;
using Tnzi.EFCore;
using Tnzi.EFCore.Data;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;
using Tnzi.Storage.Entities;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 多租户<b>开启</b>时，匿名收件人没有任何租户上下文也要能凭令牌走完整条路。
/// </summary>
/// <remarks>
/// <para>
/// <c>Signer</c> / <c>Envelope</c> / <c>FieldValue</c> / <c>FileRecord</c> 都是 <c>IMultiTenant</c>，多租户一开，
/// 全局过滤器就是 <c>TenantId == 当前租户</c>（严格等值）。收件人是匿名的：没有登录、没有 JWT，
/// 除非消费方在链接上带了 <c>?tenantId=</c>，当前租户恒为 null，令牌查询退化成
/// <c>TokenHash == hash AND TenantId IS NULL</c> —— 四个匿名端点一律答「This signing link is not valid」，
/// 而本模块此前既不给消费方任何租户提示（<c>IssuedSigningLink</c> 没有租户），文档也只字未提。
/// </para>
/// <para>
/// 令牌是 256 位随机数，等值命中就钉死了那一行 —— 它比租户上下文更强的凭据。所以令牌查询跨租户找 <c>Signer</c>，
/// 找到之后把这次请求切进那一行的租户，其余的读（请求 / 字段值 / 文件）与密封的写（新文件）都落在那个租户里。
/// 用真实的 <see cref="DataFilterManager"/> + 真实的 <see cref="CurrentTenant"/> + 真实的过滤器。
/// </para>
/// </remarks>
public class AnonymousRecipientMultiTenancyTests : SigningStorageTestBase
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
    {
        options.UseTnziMultiTenancy(true);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        var anonymous = new Mock<ICurrentUser>();
        anonymous.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymous.SetupGet(u => u.Id).Returns((Guid?)null);
        anonymous.SetupGet(u => u.TenantId).Returns((Guid?)null);
        services.AddScoped(_ => anonymous.Object);

        services.AddScoped<IDataFilterManager, DataFilterManager>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
    }

    private IDisposable AsTenant(Guid tenantId)
        => ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId);

    /// <summary>租户 A 的管理员发起并发出一份请求；返回渲染稿 id、请求 id 与令牌。</summary>
    private async Task<(Guid RenderedId, Guid RequestId, string Token)> ArrangeTenantAEnvelopeAsync()
    {
        using var _ = AsTenant(TenantA);
        return await ArrangeSentEnvelopeAsync();
    }

    private async Task<T> AcrossTenantsAsync<T>(Func<IQueryable<Envelope>, Task<T>> query)
        => await query(DbContext.Set<Envelope>().AsNoTracking().IgnoreQueryFilters());

    /// <summary>前提本身要成立：这个上下文确实开着多租户，租户行对无租户作用域确实不可见。</summary>
    [Fact]
    public async Task Precondition_a_tenant_owned_signer_is_invisible_to_a_tenantless_query()
    {
        DbContext.IsMultiTenancyEnabled.ShouldBeTrue();
        var (_, requestId, _) = await ArrangeTenantAEnvelopeAsync();

        (await AcrossTenantsAsync(q => q.SingleAsync(e => e.Id == requestId))).TenantId.ShouldBe(TenantA);
        (await DbContext.Set<Signer>().AsNoTracking().AnyAsync(s => s.RequestId == requestId)).ShouldBeFalse(
            "the ordinary query is tenant-filtered; if this passes the whole test class proves nothing");
    }

    [Fact]
    public async Task An_anonymous_signer_with_no_tenant_context_still_resolves_the_link()
    {
        var (renderedId, _, token) = await ArrangeTenantAEnvelopeAsync();

        using var request = BeginRequest();
        var packet = await request.Envelopes.GetByTokenAsync(token);

        packet.Succeeded.ShouldBeTrue(packet.Message);
        packet.Data!.Title.ShouldBe("Engagement Letter");
        packet.Data.DocumentFileId.ShouldBe(renderedId);

        var document = await request.Envelopes.GetDocumentByTokenAsync(token);
        document.Succeeded.ShouldBeTrue(document.Message);
        (await ReadAllAsync(document.Data!.Content)).ShouldBe(RenderedPdf);
    }

    [Fact]
    public async Task Submitting_without_tenant_context_seals_under_the_envelopes_tenant()
    {
        var (_, requestId, token) = await ArrangeTenantAEnvelopeAsync();

        using (var submit = BeginRequest())
        {
            var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto());
            result.Succeeded.ShouldBeTrue(result.Message);
            result.Data!.RequestStatus.ShouldBe(EnvelopeStatus.Completed);
        }

        var envelope = await AcrossTenantsAsync(q => q.SingleAsync(e => e.Id == requestId));
        envelope.Status.ShouldBe(EnvelopeStatus.Completed);
        envelope.TenantId.ShouldBe(TenantA);

        var finalPdf = await DbContext.Set<FileRecord>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(f => f.Id == envelope.FinalPdfFileId);
        finalPdf.TenantId.ShouldBe(TenantA, "the sealed product must land in the envelope's tenant, not in the null tenant");

        var certificate = await DbContext.Set<FileRecord>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(f => f.Id == envelope.CompletionCertificateFileId);
        certificate.TenantId.ShouldBe(TenantA);
    }

    /// <summary>令牌只放行它自己那一行；另一个租户的请求（哪怕请求上下文带着那个租户）读不到。</summary>
    [Fact]
    public async Task A_token_from_tenant_A_does_not_read_tenant_B_rows()
    {
        var (renderedA, _, tokenA) = await ArrangeTenantAEnvelopeAsync();
        Guid renderedB;
        using (AsTenant(TenantB))
        {
            var envelopeB = await ArrangeSentEnvelopeAsync();
            renderedB = envelopeB.RenderedId;
        }

        // 消费方把租户 B 塞进了请求上下文（错的提示）：令牌仍然钉在 A 的那一行。
        using var ambient = AsTenant(TenantB);
        using var request = BeginRequest();
        var packet = await request.Envelopes.GetByTokenAsync(tokenA);

        packet.Succeeded.ShouldBeTrue(packet.Message);
        packet.Data!.DocumentFileId.ShouldBe(renderedA);
        request.Grants.IsGranted(renderedA).ShouldBeTrue();
        request.Grants.IsGranted(renderedB).ShouldBeFalse();
    }

    /// <summary>签发链接时把租户交给消费方：多租户宿主的签署页可以据此自己带上租户。</summary>
    [Fact]
    public async Task Issued_links_carry_the_envelopes_tenant()
    {
        var (templateId, _) = await ArrangeTemplateAsyncAs(TenantA);

        using var _ = AsTenant(TenantA);
        Guid requestId;
        using (var create = BeginRequest())
        {
            var created = await create.Envelopes.CreateAsync(new CreateEnvelopeDto
            {
                TemplateId = templateId,
                Title = "Engagement Letter",
                Recipients = SingleClient(),
            });
            created.Succeeded.ShouldBeTrue(created.Message);
            requestId = created.Data!.Id;
        }

        using var send = BeginRequest();
        var sent = await send.Envelopes.SendAsync(requestId);
        sent.Succeeded.ShouldBeTrue(sent.Message);
        sent.Data!.Single().TenantId.ShouldBe(TenantA);
    }

    private async Task<(Guid TemplateId, Guid RenderedId)> ArrangeTemplateAsyncAs(Guid tenantId)
    {
        using var _ = AsTenant(tenantId);
        return await ArrangeTemplateAsync();
    }
}
