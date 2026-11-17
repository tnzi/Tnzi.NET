namespace Tnzi.System.Configuration;

/// <summary>
/// <see cref="SettingConfigurationProvider"/> 从数据库填充到哪一步了。
/// </summary>
internal enum SettingConfigurationLoadState
{
    /// <summary>还没有拿到根 ServiceProvider（宿主尚未初始化 SystemModule）。</summary>
    NotAttached,

    /// <summary>已经 attach，首次读取还没结束。</summary>
    Pending,

    /// <summary>至少成功读到过一次。之后的重载失败保持上一次的值，不回到 <see cref="Failed"/>。</summary>
    Loaded,

    /// <summary>首次读取失败、还没有成功过：配置里是 appsettings 的值，后台在重试。</summary>
    Failed,

    /// <summary>宿主没有 <c>IRepository&lt;Setting&gt;</c>（无 DbContext 的轻宿主）：没有可读的东西，也不重试。</summary>
    Unavailable,
}
