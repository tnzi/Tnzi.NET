namespace Tnzi.Finance.Services;

/// <summary>
/// 未实现汇兑损益期末重估服务
/// </summary>
/// <remarks>
/// 对外币限定的 Asset/Liability 叶子科目，按基准日汇率把交易币余额重估到本位币，
/// 差额（未实现汇兑损益）过账到 ExchangeGainLoss 科目。采用 delta-to-target 增量：
/// 重估凭证常驻账中，下次重估把上次调整计入账面基数算增量，天然收敛幂等；
/// 汇率修正 = 先冲销原凭证再重跑。AR/AP（无币种限定）刻意不在范围（结算 realized FX
/// 已按单据捕获汇率处理，GL 级重估会双计）。
/// </remarks>
public interface IRevaluationService
{
    /// <summary>预览重估结果（不过账；展示逐科目调整与净额）</summary>
    Task<Result<RevaluationPreviewDto>> PreviewAsync(RunRevaluationDto input, CancellationToken cancellationToken = default);

    /// <summary>运行重估（过账一张汇总凭证；增量全 0 时不出凭证，幂等 no-op）</summary>
    Task<Result<RevaluationPreviewDto>> RunAsync(RunRevaluationDto input, CancellationToken cancellationToken = default);
}
