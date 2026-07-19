namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 支票打印与登记服务
/// </summary>
/// <remarks>
/// 队列 = Posted Outbound + PaymentMethod==Check + 付款科目有银行档案 + 无关联 Issued 票；
/// 打印在一个 UoW 内逐张分配号→建票→渲染合并 PDF→提交（渲染失败经 UnitOfWorkAbortException 整体回滚，号码回收）。
/// 支票号占号留痕（Issued/Void/Spoiled 三态），无删除端点。
/// </remarks>
public interface ICheckService
{
    /// <summary>打印队列（可选按银行账户过滤）</summary>
    Task<Result<List<CheckQueueItemDto>>> GetQueueAsync(Guid? bankAccountId = null, CancellationToken cancellationToken = default);

    /// <summary>分页查询支票登记簿</summary>
    Task<Result<IPagedList<BankCheckDto>>> GetPagedAsync(CheckQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>打印支票（分配号 + 建票 + 渲染合并 PDF，一个事务）</summary>
    Task<Result<CheckFileDto>> PrintAsync(PrintChecksDto input, CancellationToken cancellationToken = default);

    /// <summary>登记手工支票（显式号，撞号 409）</summary>
    Task<Result<BankCheckDto>> RegisterManualAsync(RegisterManualCheckDto input, CancellationToken cancellationToken = default);

    /// <summary>作废支票（Issued → Void，号码留痕）</summary>
    Task<Result<BankCheckDto>> VoidAsync(Guid id, VoidCheckDto input, CancellationToken cancellationToken = default);

    /// <summary>登记毁票（占号留痕，推进 NextCheckNumber）</summary>
    Task<Result<BankCheckDto>> SpoilAsync(SpoilCheckDto input, CancellationToken cancellationToken = default);

    /// <summary>重打支票（原票作废 Reprinted + 新票，形成 ReplacedByCheckId 链）</summary>
    Task<Result<CheckFileDto>> ReprintAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>生成校准标尺测试页</summary>
    Task<Result<CheckFileDto>> GetCalibrationPdfAsync(Guid bankAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出 positive-pay 已开票文件（CSV）：某银行账户在 [from, to]（按签发日）内的全部支票
    /// （支票号 / 金额 / 签发日 / 收款人 / 签发或作废标志），供上送银行的支票防伪核对服务。
    /// 未在此清单中的支票（伪造/篡改）与已作废的支票被银行拒付。
    /// </summary>
    Task<Result<string>> ExportPositivePayAsync(Guid bankAccountId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>作废某付款单关联的全部 Issued 票（付款作废事件联动）</summary>
    Task<Result> VoidByPaymentAsync(Guid paymentEntryId, string reason, CancellationToken cancellationToken = default);
}
