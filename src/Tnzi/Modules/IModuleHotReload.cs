namespace Tnzi.Modules;

/// <summary>
/// 模块重初始化钩子接口
/// 实现此接口的模块可在重初始化时自定义否决、状态保存/恢复与重载后回调逻辑。
/// 未实现此接口的模块仍可被重初始化（仅重跑关闭/初始化生命周期钩子）。
/// </summary>
[ExperimentalApi(Reason = "Module re-initialization only; assembly unload/reload is not supported")]
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
