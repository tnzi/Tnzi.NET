namespace Tnzi.Signing.Services;

/// <summary>
/// 签署模板的管理面：列表 / 详情 / 增删改。
/// </summary>
/// <remarks>
/// 字段随模板**内嵌全量重建**（与 Payroll 的结构行、税级行同一范式）：模板的字段集是一个
/// 整体，逐字段增删改会让"这一版模板长什么样"取决于操作顺序。
///
/// ★ 改模板**不影响任何已发起的请求** —— 请求在发起那一刻就把模板连同字段冻结成快照
/// （<c>Envelope.TemplateSnapshotJson</c>）。所以这里可以放心改，也因此
/// <see cref="Entities.EnvelopeTemplate.Version"/> 每次保存递增，让快照能标注来源版本。
/// </remarks>
[ExperimentalApi(Reason = "E-signature contracts are shaped by a single consumer so far; they may change before a second one validates them")]
public interface IEnvelopeTemplateService
{
    /// <summary>分页列表。</summary>
    Task<Result<IPagedList<EnvelopeTemplateListDto>>> GetPagedAsync(EnvelopeTemplateQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>详情（含字段）。</summary>
    Task<Result<EnvelopeTemplateDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>新建。</summary>
    Task<Result<EnvelopeTemplateDto>> CreateAsync(CreateEnvelopeTemplateDto input, CancellationToken cancellationToken = default);

    /// <summary>更新（字段整体重建，版本号 +1）。</summary>
    Task<Result<EnvelopeTemplateDto>> UpdateAsync(Guid id, UpdateEnvelopeTemplateDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除。
    /// </summary>
    /// <remarks>
    /// ★ 被任何请求引用过的模板**拒绝删除**（409），哪怕请求早已完成：模板 id 是那份
    /// 快照的出处，删掉之后"这份文件是照哪个模板出的"就再也答不上来。要停用请改
    /// <c>IsActive</c>。
    /// </remarks>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
