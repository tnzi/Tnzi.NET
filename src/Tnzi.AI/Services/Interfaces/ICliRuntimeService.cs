namespace Tnzi.AI.Services;

/// <summary>
/// 外部运行时注册表的管理面。
/// </summary>
public interface ICliRuntimeService
{
    /// <summary>列出已注册的外部运行时。</summary>
    Task<Result<List<CliRuntimeDto>>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>取单个运行时。</summary>
    Task<Result<CliRuntimeDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 立即探测本宿主 PATH 上的 CLI 并注册/更新运行时。
    /// 后台服务也会定时做同一件事；这是给管理员的手动触发口。
    /// </summary>
    Task<Result<CliRuntimeProbeResultDto>> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>更新管理员可改的字段。</summary>
    Task<Result<CliRuntimeDto>> UpdateAsync(
        Guid id, UpdateCliRuntimeDto input, CancellationToken cancellationToken = default);

    /// <summary>删除运行时注册。</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>列出本部署可用的 provider 描述（内置表 + appsettings 扩展合并）。</summary>
    Task<Result<List<CliProviderOptionDto>>> GetProviderOptionsAsync(CancellationToken cancellationToken = default);
}
