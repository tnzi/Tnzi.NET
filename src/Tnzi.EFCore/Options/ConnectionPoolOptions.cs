namespace Tnzi.EFCore.Options;

/// <summary>
/// 数据库连接池配置选项
/// 用于配置数据库连接池参数，提升高并发场景下的性能
/// </summary>
public class ConnectionPoolOptions
{
    /// <summary>
    /// 最大连接池大小
    /// 默认：null（使用数据库驱动默认值，通常为100）
    /// </summary>
    public int? MaxPoolSize { get; set; }

    /// <summary>
    /// 最小连接池大小
    /// 默认：null（使用数据库驱动默认值，通常为0）
    /// </summary>
    public int? MinPoolSize { get; set; }

    /// <summary>
    /// 连接超时时间（秒）
    /// 默认：null（使用数据库驱动默认值，通常为15-30秒）
    /// </summary>
    public int? ConnectionTimeout { get; set; }

    /// <summary>
    /// 命令超时时间（秒）
    /// 默认：null（使用数据库驱动默认值，通常为30秒）
    /// </summary>
    public int? CommandTimeout { get; set; }

    /// <summary>
    /// 连接空闲超时时间（秒）
    /// 用于 MySQL 等数据库的连接生命周期管理
    /// 默认：null（使用数据库驱动默认值）
    /// </summary>
    public int? ConnectionIdleTimeout { get; set; }

    /// <summary>
    /// 是否启用连接池
    /// 默认：true
    /// </summary>
    public bool Pooling { get; set; } = true;

    /// <summary>
    /// 是否有任何连接池配置
    /// </summary>
    public bool HasConfiguration =>
        MaxPoolSize.HasValue ||
        MinPoolSize.HasValue ||
        ConnectionTimeout.HasValue ||
        CommandTimeout.HasValue ||
        ConnectionIdleTimeout.HasValue ||
        !Pooling;
}

