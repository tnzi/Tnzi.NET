namespace Tnzi.System.Services;

/// <summary>
/// 配置中心服务 - 聚合各模块注册的 ISettingDefinitionProvider，
/// 提供 schema + 生效值查询、按组保存与恢复默认。
/// </summary>
public interface ISettingsCenterService
{
    Task<Result<List<SettingsCenterGroupDto>>> GetDefinitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按组批量保存。changedValues 仅含变更字段；value=null 表示删除该字段覆盖（回退默认）。
    /// 部分失败语义：各字段独立持久化（每次写入自带 UoW 与变更事件），中途失败时
    /// 已写入的字段保持已提交状态、不回滚；每次写入幂等，调用方重发整组即可安全重试。
    /// </summary>
    Task<Result<SettingsCenterGroupDto>> SaveGroupAsync(string groupKey, Dictionary<string, string?> changedValues, CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复默认：删除该组全部字段的 Setting 覆盖行。
    /// 与 SaveGroupAsync 相同的部分失败语义：逐行删除、不回滚，重试安全。
    /// </summary>
    Task<Result<SettingsCenterGroupDto>> ResetGroupAsync(string groupKey, CancellationToken cancellationToken = default);
}
