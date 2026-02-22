namespace Tnzi.Modules;

/// <summary>
/// 可配置表名前缀的模块接口
/// </summary>
public interface IHasTableNamePrefix
{
    /// <summary>
    /// 表名前缀
    /// </summary>
    string? TableNamePrefix { get; }
}
