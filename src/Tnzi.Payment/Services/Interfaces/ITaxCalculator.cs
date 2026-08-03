namespace Tnzi.Payment.Services;

/// <summary>
/// 税额计算器。支付与发票在落库前经它算出税额与应付总额。
/// </summary>
/// <remarks>
/// 默认实现按 <c>Payment:Tax</c> 的固定税率计算（见 <see cref="DefaultTaxCalculator"/>）。
/// 需要按辖区/客户税号做精确计税（Stripe Tax、Avalara、各国增值税规则）的应用，
/// 在自己的模块里注册一个实现覆盖即可，支付链路无需改动。
/// </remarks>
public interface ITaxCalculator
{
    /// <summary>
    /// 计算税额与应付总额
    /// </summary>
    Task<Result<TaxCalculationResult>> CalculateAsync(TaxCalculationRequest request, CancellationToken cancellationToken = default);
}
