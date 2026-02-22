namespace Tnzi.EFCore.Options;

/// <summary>
/// 数据授权配置选项
/// </summary>
public class DataAuthOptions
{
    /// <summary>
    /// 是否自动应用数据授权（默认：false，需要手动调用WithDataAuth扩展方法）
    /// </summary>
    /// <remarks>
    /// 由于数据授权服务是异步的，而IQueryable是同步的，自动应用需要在查询执行时进行。
    /// 如果设置为true，Repository的查询方法会自动应用数据授权过滤器。
    /// </remarks>
    public bool AutoApplyDataAuth { get; set; } = false;
}

