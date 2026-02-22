namespace Tnzi.Data.Snows;

/// <summary>
/// 雪花算法工作器接口
/// </summary>
internal interface ISnowWorker
{
    Action<OverCostActionArg>? GenAction { get; set; }

    long NextId();
}

