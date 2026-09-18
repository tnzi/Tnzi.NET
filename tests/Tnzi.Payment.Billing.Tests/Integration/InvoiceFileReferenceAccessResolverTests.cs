using Tnzi.Security.Claims;
using Tnzi.Storage;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 存储模块问「按这条发票记录看，当前用户能不能读它引用的文件」时的回答。
/// </summary>
/// <remarks>
/// 发票产物多半在 webhook / 后台生成，存储侧的 <c>CreatorId</c> 为 null —— 没有这个解析器，客户下载自己的发票
/// 会被存储判 404，客户重发发票时通知模块按探针问「读得到吗」也会被答否。
/// </remarks>
public class InvoiceFileReferenceAccessResolverTests : BillingIntegrationTestBase
{
    private static readonly Guid Owner = Guid.NewGuid();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IPermissionChecker> _permissions = new();

    public InvoiceFileReferenceAccessResolverTests()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _permissions.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped(_ => _currentUser.Object);
        services.AddScoped(_ => _permissions.Object);
        services.AddScoped<IReadOnlyRepository<Invoice, Guid>>(sp => sp.GetRequiredService<IRepository<Invoice, Guid>>());
        services.AddScoped<IFileReferenceAccessResolver, InvoiceFileReferenceAccessResolver>();
    }

    [Fact]
    public void HandlesOnlyInvoiceReferences()
    {
        using var scope = ServiceProvider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IFileReferenceAccessResolver>();

        resolver.CanHandle(nameof(Invoice)).ShouldBeTrue();
        resolver.CanHandle("Payment").ShouldBeFalse();
    }

    /// <summary>★ 归属用户读得到自己的发票产物（产物的 CreatorId 与他无关）。</summary>
    [Fact]
    public async Task TheInvoiceOwner_CanReadItsDocument()
    {
        var invoice = await SeedInvoiceAsync(Owner);
        _currentUser.Setup(u => u.Id).Returns(Owner);

        (await CanReadAsync(invoice)).ShouldBeTrue();
    }

    /// <summary>别人的发票读不到；没有 payment.invoice.view 也读不到。</summary>
    [Fact]
    public async Task AStranger_CannotReadIt()
    {
        var invoice = await SeedInvoiceAsync(Owner);
        _currentUser.Setup(u => u.Id).Returns(Guid.NewGuid());

        (await CanReadAsync(invoice)).ShouldBeFalse();
    }

    /// <summary>持 payment.invoice.view 的管理员读得到任何发票的产物（与管理端点的类级门同一个码）。</summary>
    [Fact]
    public async Task AnInvoiceViewer_CanReadAnyInvoiceDocument()
    {
        var invoice = await SeedInvoiceAsync(Owner);
        _currentUser.Setup(u => u.Id).Returns(Guid.NewGuid());
        _permissions.Setup(p => p.IsGrantedAsync("payment.invoice.view")).ReturnsAsync(true);

        (await CanReadAsync(invoice)).ShouldBeTrue();
    }

    [Fact]
    public async Task AnAnonymousCaller_CannotReadIt()
    {
        var invoice = await SeedInvoiceAsync(Owner);
        _currentUser.Setup(u => u.Id).Returns((Guid?)null);

        (await CanReadAsync(invoice)).ShouldBeFalse();
    }

    [Fact]
    public async Task AReferenceToAMissingInvoice_IsRefused()
    {
        _currentUser.Setup(u => u.Id).Returns(Owner);

        using var scope = ServiceProvider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IFileReferenceAccessResolver>();
        (await resolver.CanReadAsync(new FileReferenceDescriptor(Guid.NewGuid(), nameof(Invoice), Guid.NewGuid(), nameof(Invoice.PdfFileId)))).ShouldBeFalse();
    }

    private async Task<bool> CanReadAsync(Invoice invoice)
    {
        using var scope = ServiceProvider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IFileReferenceAccessResolver>();
        return await resolver.CanReadAsync(new FileReferenceDescriptor(Guid.NewGuid(), nameof(Invoice), invoice.Id, nameof(Invoice.PdfFileId)));
    }

    private async Task<Invoice> SeedInvoiceAsync(Guid userId)
    {
        var invoice = new Invoice
        {
            InvoiceNo = $"INV{Guid.NewGuid():N}",
            UserId = userId,
            CustomerName = "Ada",
            CustomerEmail = "ada@example.com",
            Currency = "USD",
            Status = InvoiceStatus.Draft,
            InvoiceDate = DateTime.UtcNow
        };
        await SeedAsync(invoice);
        return invoice;
    }
}
