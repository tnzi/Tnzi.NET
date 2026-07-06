namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 税模型服务（机构/税率/税码；不含任何辖区合规内容）
/// </summary>
public interface ITaxService
{
    // ── 税务机构 ──────────────────────────────────────────────

    /// <summary>获取全部税务机构</summary>
    Task<Result<List<TaxAgencyDto>>> GetAgenciesAsync(CancellationToken cancellationToken = default);

    /// <summary>创建税务机构</summary>
    Task<Result<TaxAgencyDto>> CreateAgencyAsync(UpsertTaxAgencyDto input, CancellationToken cancellationToken = default);

    /// <summary>更新税务机构</summary>
    Task<Result<TaxAgencyDto>> UpdateAgencyAsync(Guid id, UpsertTaxAgencyDto input, CancellationToken cancellationToken = default);

    /// <summary>删除税务机构（被税率引用时拒绝）</summary>
    Task<Result> DeleteAgencyAsync(Guid id, CancellationToken cancellationToken = default);

    // ── 税率 ─────────────────────────────────────────────────

    /// <summary>获取税率列表（可按机构过滤）</summary>
    Task<Result<List<TaxRateDto>>> GetRatesAsync(Guid? agencyId = null, CancellationToken cancellationToken = default);

    /// <summary>创建税率</summary>
    Task<Result<TaxRateDto>> CreateRateAsync(UpsertTaxRateDto input, CancellationToken cancellationToken = default);

    /// <summary>更新税率</summary>
    Task<Result<TaxRateDto>> UpdateRateAsync(Guid id, UpsertTaxRateDto input, CancellationToken cancellationToken = default);

    /// <summary>删除税率（被税码组件引用时拒绝）</summary>
    Task<Result> DeleteRateAsync(Guid id, CancellationToken cancellationToken = default);

    // ── 税码 ─────────────────────────────────────────────────

    /// <summary>获取全部税码（含组件）</summary>
    Task<Result<List<TaxCodeDto>>> GetCodesAsync(CancellationToken cancellationToken = default);

    /// <summary>创建税码（含组件）</summary>
    Task<Result<TaxCodeDto>> CreateCodeAsync(UpsertTaxCodeDto input, CancellationToken cancellationToken = default);

    /// <summary>更新税码（组件全量替换）</summary>
    Task<Result<TaxCodeDto>> UpdateCodeAsync(Guid id, UpsertTaxCodeDto input, CancellationToken cancellationToken = default);

    /// <summary>删除税码（软删除；P2b 起被单据引用时拒绝）</summary>
    Task<Result> DeleteCodeAsync(Guid id, CancellationToken cancellationToken = default);
}
