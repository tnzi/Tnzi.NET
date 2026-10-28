using Mapster;
using MapsterMapper;
using Tnzi.Domain.Entities;
using Tnzi.Mapster;
using Tnzi.Payment.Services;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 开票子模块集成测试基类：真实 SQLite + 仓储 + 发票服务。
/// </summary>
/// <remarks>
/// 这里<b>不</b>注册 <c>ITemplateRenderService</c> / <c>IHtmlToPdfConverter</c> /
/// <c>INotificationService</c> / <c>IFileStorageService</c> —— 那四个在服务里本来就是可空可选注入，
/// 不注册跑的正是「只加载了本模块，没加载模板 / 通知 / 存储」的最小宿主。
/// 发票照常开得出来，只是 PDF 落本地回退路径、发送直接失败并说清原因。
/// </remarks>
public abstract class BillingIntegrationTestBase : IntegratedTestBase<BillingTestDbContext>
{
    protected BillingIntegrationTestBase()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions();
        services.Configure<InvoiceOptions>(o =>
        {
            o.Enabled = true;
            o.DefaultTemplate = "InvoiceDefault";
            o.CompanyName = "Test Co";
        });
        services.Configure<PaymentOptions>(o => o.DefaultCurrency = "USD");

        AddRepo<PaymentEntity>(services);
        AddRepo<Invoice>(services);
        AddRepo<InvoiceLineItem>(services);

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(BillingTestDbContext)]);
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        services.AddScoped<IBillingAccountResolver, DefaultBillingAccountResolver>();
        services.AddScoped<IPaymentInvoiceService, PaymentInvoiceService>();
    }

    protected static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<BillingTestDbContext, TEntity, Guid>(sp.GetRequiredService<BillingTestDbContext>(), serviceProvider: sp));
    }

    /// <summary>在独立 scope 中持久化种子实体（模拟每请求独立 DbContext）。</summary>
    /// <remarks>
    /// 没写计费账户的种子行按经办人的个人账户补齐：与迁移回填对既有行做的事相同。
    /// </remarks>
    protected async Task SeedAsync(params object[] entities)
    {
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case PaymentEntity p when string.IsNullOrEmpty(p.BillingAccountType) && p.UserId.HasValue:
                    p.BillingAccountType = BillingAccountTypes.User;
                    p.BillingAccountId = p.UserId.Value.ToString();
                    break;
                case Invoice i when string.IsNullOrEmpty(i.BillingAccountType) && i.UserId.HasValue:
                    i.BillingAccountType = BillingAccountTypes.User;
                    i.BillingAccountId = i.UserId.Value.ToString();
                    break;
            }
        }

        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BillingTestDbContext>();
        ctx.AddRange(entities);
        await ctx.SaveChangesAsync();
    }

    /// <summary>在独立 scope 中执行一次服务操作（每次操作 = 一个新的 DbContext）。</summary>
    protected async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        using var scope = ServiceProvider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>在独立 scope 中读取实体最新状态（绕过身份映射缓存）。</summary>
    protected async Task<TEntity?> ReloadAsync<TEntity>(Guid id) where TEntity : class, IEntity<Guid>
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<TEntity, Guid>>();
        return await repo.FirstOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>一笔已收款的支付 —— 自动开票的唯一合法来源。</summary>
    protected static PaymentEntity SucceededPayment(decimal amount = 100m) => new()
    {
        TradeNo = $"T{Guid.NewGuid():N}",
        BusinessOrderNo = "ORD-1",
        ChannelCode = PaymentConstants.OfflineChannelCode,
        Currency = "USD",
        Status = PaymentStatus.Succeeded,
        OriginalAmount = amount,
        PayableAmount = amount,
        PaidAmount = amount,
        PaidTime = DateTime.UtcNow,
        CustomerName = "Ada",
        CustomerEmail = "ada@example.com",
        Description = "One licence",
    };
}
