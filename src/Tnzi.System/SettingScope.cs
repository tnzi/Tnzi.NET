namespace Tnzi.System;

/// <summary>
/// 配置项作用域
/// </summary>
public enum SettingScope
{
    /// <summary>全局配置</summary>
    Global = 0,
    /// <summary>租户级配置</summary>
    Tenant = 1,
    /// <summary>用户级配置</summary>
    User = 2
}
