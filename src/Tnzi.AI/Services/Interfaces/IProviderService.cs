namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// AI Provider 实体 CRUD 服务接口
/// </summary>
/// <remarks>
/// 该服务管理 Provider 实体（数据库驱动的提供商定义），与 appsettings 中的
/// <see cref="Tnzi.AI.Options.ProviderOptions"/> 配置共存：
/// <list type="bullet">
///   <item>数据库为空时，<c>IChatClientFactory</c> 回退到配置驱动行为；</item>
///   <item>数据库存在已启用记录时，作为 override/补充层供 factory 合并查询。</item>
/// </list>
/// API Key 通过 <c>IDataProtectionProvider</c> 加密，永远不会通过 DTO 暴露明文或密文。
/// </remarks>
public interface IProviderService
{
    /// <summary>分页查询 Provider 列表</summary>
    Task<Result<IPagedList<ProviderDto>>> GetPagedListAsync(ProviderQueryDto query, CancellationToken ct = default);

    /// <summary>按 ID 获取 Provider</summary>
    Task<Result<ProviderDto>> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>创建 Provider</summary>
    Task<Result<ProviderDto>> CreateAsync(CreateProviderDto dto, CancellationToken ct = default);

    /// <summary>更新 Provider - ApiKey 为 null 时保留现有密文</summary>
    Task<Result<ProviderDto>> UpdateAsync(Guid id, UpdateProviderDto dto, CancellationToken ct = default);

    /// <summary>软删除 Provider</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>测试 Provider 连接（轻量探针）</summary>
    Task<Result<ProviderTestResultDto>> TestConnectionAsync(Guid id, CancellationToken ct = default);

    /// <summary>获取启用的 Provider 下拉选项（供前端 Agent 配置下拉框）</summary>
    Task<Result<List<ProviderOptionDto>>> GetOptionsAsync(CancellationToken ct = default);

    /// <summary>
    /// 列出指定 Provider 可用的模型 - 优先调用第三方 OpenAI 兼容 <c>/v1/models</c>，
    /// 失败时按 ProviderType 返回静态精选兜底列表。
    /// </summary>
    Task<Result<ProviderModelsDto>> ListModelsAsync(Guid id, CancellationToken ct = default);
}
