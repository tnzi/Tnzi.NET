namespace Tnzi.Modules;

/// <summary>
/// 模块重载器接口 —— 负责模块的重初始化（re-initialization），而非程序集热重载。
/// 重初始化即按序重跑目标模块的 <c>OnApplicationShutdownAsync</c> → <c>OnApplicationInitializationAsync</c>
/// 生命周期钩子（不卸载程序集、不重注册 DI 服务）。默认关闭。
/// </summary>
[ExperimentalApi(Reason = "Module re-initialization only; assembly unload/reload is not supported")]
public interface IModuleReloader
{
    /// <summary>
    /// 开始监听文件变化
    /// </summary>
    Task StartWatchingAsync();

    /// <summary>
    /// 停止监听文件变化
    /// </summary>
    Task StopWatchingAsync();

    /// <summary>
    /// 手动触发重初始化指定模块（重跑其关闭/初始化生命周期钩子）
    /// </summary>
    /// <param name="moduleType">模块类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> ReloadModuleAsync(Type moduleType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否正在监听
    /// </summary>
    bool IsWatching { get; }
}
