namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 发薪批次服务（草稿 CRUD + 计算 → 过账 → 付款 → 作废全周期 + 外部摄取）
/// </summary>
/// <remarks>
/// 过账/付款/作废全部经 Finance 的 <c>ILedgerPostingService</c> 扩展面
/// （PostAsync/ReverseAsync/GetBySourceAsync），外层 <c>ExecuteInUnitOfWorkAsync</c> +
/// <c>UnitOfWorkAbortException</c> 保证原子性；凭证号在最后可失败步骤之后分配。
/// </remarks>
public interface IPayRunService
{
    /// <summary>分页查询发薪批次</summary>
    Task<Result<IPagedList<PayRunListDto>>> GetPagedAsync(PayRunQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取发薪批次</summary>
    Task<Result<PayRunDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建发薪批次草稿</summary>
    Task<Result<PayRunDto>> CreateAsync(CreatePayRunDto input, CancellationToken cancellationToken = default);

    /// <summary>更新发薪批次草稿（仅 Draft 态）</summary>
    Task<Result<PayRunDto>> UpdateAsync(Guid id, UpdatePayRunDto input, CancellationToken cancellationToken = default);

    /// <summary>删除发薪批次草稿（仅 Draft 态；payslips 级联删除）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>计算/重算（Draft|Calculated → Calculated；旧 payslips 重建，Error 不炸整批）</summary>
    Task<Result<PayRunDto>> CalculateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>过账（Calculated → Posted；有 Error 拒绝；行数分块多凭证）</summary>
    Task<Result<PayRunDto>> PostAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>付款（Posted|PartiallyPaid → PartiallyPaid|Paid；可多次累进）</summary>
    Task<Result<PayRunDto>> PayAsync(Guid id, PayRunPaymentDto input, CancellationToken cancellationToken = default);

    /// <summary>作废（Posted 及之后 → Voided；付款先、过账后全冲销）</summary>
    Task<Result<PayRunDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>列出批次的工资单（不含行）</summary>
    Task<Result<List<PayslipListDto>>> GetPayslipsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>获取单张工资单（含行）</summary>
    Task<Result<PayslipDto>> GetPayslipAsync(Guid id, Guid payslipId, CancellationToken cancellationToken = default);

    /// <summary>修改单张工资单输入并单独重算（仅 Calculated 态）</summary>
    Task<Result<PayslipDto>> UpdatePayslipInputsAsync(Guid id, Guid payslipId, UpdatePayslipInputsDto input, CancellationToken cancellationToken = default);

    /// <summary>外部批次幂等摄取（External/OpeningBalance；AutoPost 半完成自愈）</summary>
    Task<Result<PayRunDto>> CreateFromExternalAsync(ExternalPayRunIngestDto input, CancellationToken cancellationToken = default);
}
