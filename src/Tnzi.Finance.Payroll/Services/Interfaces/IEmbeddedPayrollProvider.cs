namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// Embedded 薪酬提供者契约（Check/Gusto Embedded 形态；v1 仅定契约 + 摄取，不做编排）
/// </summary>
/// <remarks>
/// 消费应用可实现并注册以对接外部代发/代缴服务；提交后经 <see cref="GetRunResultAsync"/>
/// 取回结果，映射为 <see cref="ExternalPayRunIngestDto"/> 交
/// <c>IPayRunService.CreateFromExternalAsync</c> 幂等摄取（框架不反向编排提供者）。
/// </remarks>
public interface IEmbeddedPayrollProvider
{
    /// <summary>提供者代码</summary>
    string ProviderCode { get; }

    /// <summary>提交一个发薪批次到外部提供者</summary>
    Task<Result<ExternalPayRunSubmission>> SubmitRunAsync(EmbeddedPayRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>查询外部批次状态</summary>
    Task<Result<ExternalPayRunStatusDto>> GetRunStatusAsync(string providerRunId, CancellationToken cancellationToken = default);

    /// <summary>取回外部批次计算结果</summary>
    Task<Result<ExternalPayRunResultDto>> GetRunResultAsync(string providerRunId, CancellationToken cancellationToken = default);
}
