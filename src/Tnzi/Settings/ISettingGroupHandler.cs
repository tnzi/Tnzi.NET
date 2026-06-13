namespace Tnzi.Settings;

/// <summary>
/// 配置组保存钩子（可选）。承接保存前校验与保存后副作用 —
/// 典型场景：cron 字段保存时重注册 Hangfire 任务。
/// 与 ISettingDefinitionProvider 一样按 DI 注册，按 GroupKey 匹配。
/// </summary>
[ExperimentalApi(Reason = "Settings center contract is new and may evolve")]
public interface ISettingGroupHandler
{
    string GroupKey { get; }

    /// <summary>保存前校验。返回 Fail 则整组保存中止，错误透传给前端。</summary>
    Task<Result> ValidateAsync(IReadOnlyDictionary<string, string?> changedValues, CancellationToken cancellationToken = default);

    /// <summary>持久化成功后回调（副作用挂这里）。默认空实现。</summary>
    Task OnSavedAsync(IReadOnlyDictionary<string, string?> changedValues, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
