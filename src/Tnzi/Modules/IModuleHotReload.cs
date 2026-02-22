namespace Tnzi.Modules;

/// <summary>
/// 模块热重载接口
/// 实现此接口的模块可以支持热重载功能
/// </summary>
[ExperimentalApi(Reason = "Hot reload is not fully implemented")]
public interface IModuleHotReload
{
    /// <summary>
    /// 重载前调用（用于保存状态、清理资源等）
    /// </summary>
    /// <returns>是否允许重载</returns>
    Task<bool> OnBeforeReloadAsync();

    /// <summary>
    /// 重载后调用（用于恢复状态、重新初始化等）
    /// </summary>
    Task OnAfterReloadAsync();

    /// <summary>
    /// 获取需要保存的状态（用于重载后恢复）
    /// </summary>
    /// <returns>状态对象（将被序列化为JSON）</returns>
    Task<object?> GetStateAsync();

    /// <summary>
    /// 恢复状态（重载后调用）
    /// </summary>
    /// <param name="state">之前保存的状态对象</param>
    Task RestoreStateAsync(object? state);
}
