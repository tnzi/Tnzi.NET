namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// 薪资模块对 <see cref="IMasterDataUsageProvider"/> 的实现：薪资组件的费用 / 负债科目是每次
/// 过账工资都要写的科目，所以核心的科目删除守卫在这里也问得到它们。
/// </summary>
/// <remarks>
/// 手法与 <c>Tnzi.Finance.Offers</c> / <c>Tnzi.Finance.Recurring</c> 的同名实现一致：内核只认契约，
/// 本模块回答事实；未加载本模块时无实现 = 「无人在用」，只会少拒绝不会放宽任何守卫。
/// 一个只被组件指着、还没跑过工资的科目在核心的分录检查里是干净的，删掉后下一次过账工资才撞上
/// 「科目不存在」—— 失败是响亮的，但报错的时刻与删科目的人已经无关。
/// 停用的组件同样算数：停用可逆，重新启用时科目要还在。
/// </remarks>
public class PayrollMasterDataUsageProvider : IMasterDataUsageProvider
{
    private readonly IReadOnlyRepository<SalaryComponent, Guid> _componentRepository;

    public PayrollMasterDataUsageProvider(IReadOnlyRepository<SalaryComponent, Guid> componentRepository)
    {
        _componentRepository = Check.NotNull(componentRepository);
    }

    /// <inheritdoc />
    public async Task<MasterDataUsage?> FindUsageAsync(
        FinanceMasterDataKind kind, Guid id, CancellationToken cancellationToken = default)
    {
        switch (kind)
        {
            case FinanceMasterDataKind.Account:
                return await _componentRepository.AnyAsync(
                        c => c.ExpenseAccountId == id || c.LiabilityAccountId == id, cancellationToken)
                    ? new MasterDataUsage("Cannot delete an account referenced by a salary component. Edit the component first.")
                    : null;

            default:
                // 不认识的种类交给别的实现回答，而不是猜一个「没在用」以外的答案。
                return null;
        }
    }
}
