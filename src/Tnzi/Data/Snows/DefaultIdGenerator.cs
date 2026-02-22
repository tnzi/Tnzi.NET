namespace Tnzi.Data.Snows;

/// <summary>
/// 默认实现
/// </summary>
public class DefaultIdGenerator : IIdGenerator
{
    private ISnowWorker _snowWorker { get; set; }

    public Action<OverCostActionArg>? GenIdActionAsync
    {
        get => _snowWorker.GenAction;
        set => _snowWorker.GenAction = value;
    }


    public DefaultIdGenerator(IdGeneratorOptions options)
    {
        if (options == null)
        {
            throw new InfrastructureException("IdGenerator", "options error.");
        }

        if (options.BaseTime < DateTime.Now.AddYears(-50) || options.BaseTime > DateTime.Now)
        {
            throw new InfrastructureException("IdGenerator", "BaseTime error.");
        }

        if (options.SeqBitLength + options.WorkerIdBitLength > 22)
        {
            throw new InfrastructureException("IdGenerator", "error: WorkerIdBitLength + SeqBitLength <= 22");
        }

        var maxWorkerIdNumber = Math.Pow(2, options.WorkerIdBitLength) - 1;
        if (options.WorkerId < 0 || options.WorkerId > maxWorkerIdNumber)
        {
            throw new InfrastructureException("IdGenerator", "WorkerId error. (range:[1, " + maxWorkerIdNumber + "]");
        }

        if (options.SeqBitLength < 2 || options.SeqBitLength > 21)
        {
            throw new InfrastructureException("IdGenerator", "SeqBitLength error. (range:[2, 21])");
        }

        var maxSeqNumber = Math.Pow(2, options.SeqBitLength) - 1;
        if (options.MaxSeqNumber < 0 || options.MaxSeqNumber > maxSeqNumber)
        {
            throw new InfrastructureException("IdGenerator", "MaxSeqNumber error. (range:[1, " + maxSeqNumber + "]");
        }

        var maxValue = maxSeqNumber; // maxSeqNumber - 1;
        if (options.MinSeqNumber < 1 || options.MinSeqNumber > maxValue)
        {
            throw new InfrastructureException("IdGenerator", "MinSeqNumber error. (range:[1, " + maxValue + "]");
        }

        switch (options.Method)
        {
            case 1:
                _snowWorker = new SnowWorkerM1(options);
                break;
            case 2:
                _snowWorker = new SnowWorkerM2(options);
                break;
            default:
                _snowWorker = new SnowWorkerM1(options);
                break;
        }

        // Method 1 (漂移算法) 需要延迟以确保时间戳的唯一性
        // 注意：构造函数中无法使用异步方法，因此使用 Thread.Sleep
        // 这是必要的初始化逻辑，用于确保 ID 生成器的时间戳正确初始化
        if (options.Method == 1)
        {
            Thread.Sleep(500);
        }
    }


    public long NewLong()
    {
        return _snowWorker.NextId();
    }
}
