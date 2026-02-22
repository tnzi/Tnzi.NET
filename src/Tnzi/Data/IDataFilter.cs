namespace Tnzi.Data;

/// <summary>
/// 数据过滤器标记接口
/// </summary>
public interface IDataFilter
{
}

/// <summary>
/// 软删除过滤器标记
/// </summary>
public interface ISoftDeleteFilter : IDataFilter
{
}

/// <summary>
/// 多租户过滤器标记
/// </summary>
public interface IMultiTenantFilter : IDataFilter
{
}

