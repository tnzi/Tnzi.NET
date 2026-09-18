namespace Tnzi.Payment.Services;

/// <summary>
/// 父模块贡献的租户来源：有未终结支付（待关过期）或在途退款（待对账）的租户。
/// </summary>
/// <remarks>
/// 跨租户读取只取 <c>TenantId</c> 一列、只看未终结的行，不把任何一行业务数据带出当前租户。
/// 只在多租户开启时被调用（<see cref="IPaymentTenantSource"/> 的约定），因此这里可以引用 <c>TenantId</c> 列。
/// </remarks>
public class PaymentTenantSource : IPaymentTenantSource
{
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IRepository<Refund, Guid> _refundRepository;

    public PaymentTenantSource(IRepository<PaymentEntity, Guid> paymentRepository, IRepository<Refund, Guid> refundRepository)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _refundRepository = Check.NotNull(refundRepository);
    }

    public async Task<IReadOnlyList<Guid?>> GetTenantIdsAsync(CancellationToken cancellationToken = default)
    {
        var paymentTenants = await _paymentRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing))
            .Select(p => p.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var refundTenants = await _refundRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(r => !r.IsDeleted && r.Status == RefundStatus.Refunding)
            .Select(r => r.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return paymentTenants.Concat(refundTenants).Distinct().ToList();
    }
}
