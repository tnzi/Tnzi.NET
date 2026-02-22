namespace Tnzi.Data.Snows;

/// <summary>
/// ID生成器接口
/// </summary>
public interface IIdGenerator
{
    /// <summary>
    /// 生成过程中产生的事件
    /// </summary>
    Action<OverCostActionArg>? GenIdActionAsync { get; set; }

    /// <summary>
    /// 生成新的long型Id
    /// </summary>
    /// <returns></returns>
    long NewLong();

    // Guid NewGuid();
}

