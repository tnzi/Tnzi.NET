namespace Tnzi.Modules;

/// <summary>
/// 模块重载器接口
/// 负责模块的热重载功能
/// </summary>
[ExperimentalApi(Reason = "Hot reload is not fully implemented")]
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
    /// 手动触发重载指定模块
    /// </summary>
    /// <param name="moduleType">模块类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> ReloadModuleAsync(Type moduleType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否正在监听
    /// </summary>
    bool IsWatching { get; }
}
