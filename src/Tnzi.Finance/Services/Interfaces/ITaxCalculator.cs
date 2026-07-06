namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 税额计算器（可插拔：默认实现为行级百分比 + 复合税；
/// 消费应用可整体替换以接入外部税务引擎）
/// </summary>
/// <remarks>
/// 行级计算：每行按其税码的组件依次计税并舍入（AwayFromZero），
/// 结果按税率维度聚合。停用/不存在的税码抛 <see cref="Tnzi.Exceptions.BusinessException"/>，
/// 由单据服务捕获转 Result。
/// </remarks>
public interface ITaxCalculator
{
    /// <summary>计算税额</summary>
    Task<TaxCalculationResult> CalculateAsync(TaxCalculationRequest request, CancellationToken cancellationToken = default);
}
