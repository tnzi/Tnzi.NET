namespace Tnzi.Domain.Entities;

/// <summary>
/// 乐观并发控制接口
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IConcurrencyStamp
{
    /// <summary>
    /// 并发标记（每次更新自动变更）
    /// </summary>
    string ConcurrencyStamp { get; set; }
}
