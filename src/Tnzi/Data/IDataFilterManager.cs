namespace Tnzi.Data;

/// <summary>
/// 数据过滤器管理器接口
/// </summary>
public interface IDataFilterManager
{
    /// <summary>
    /// 禁用过滤器（返回IDisposable，using语句自动恢复）
    /// </summary>
    IDisposable Disable<TFilter>() where TFilter : class, IDataFilter;

    /// <summary>
    /// 启用过滤器（返回IDisposable，using语句自动恢复）
    /// </summary>
    IDisposable Enable<TFilter>() where TFilter : class, IDataFilter;

    /// <summary>
    /// 检查过滤器是否启用
    /// </summary>
    bool IsEnabled<TFilter>() where TFilter : class, IDataFilter;
}

