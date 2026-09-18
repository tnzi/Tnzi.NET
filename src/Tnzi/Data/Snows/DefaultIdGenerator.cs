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

        // BaseTime 的契约是 UTC（见 IdGeneratorOptions.BaseTime），必须与 UtcNow 比较：
        // 与本地时间 DateTime.Now 比较会带上时区偏移，在 UTC 负偏移时区把接近"现在"的合法 BaseTime 误判为超前
        var utcNow = DateTime.UtcNow;
        if (options.BaseTime < utcNow.AddYears(-50) || options.BaseTime > utcNow)
        {
            throw new InfrastructureException("IdGenerator", "BaseTime error.");
        }

        if (options.SeqBitLength + options.WorkerIdBitLength > 22)
        {
            throw new InfrastructureException("IdGenerator", "error: WorkerIdBitLength + SeqBitLength <= 22");
        }

        // 0 不是机器码：SnowWorkerM1 此前把 0 替换成 DateTime.Now.Millisecond（0-999），6 位字段装不下 ⇒ 溢进时间戳位、
        // 与别的机器码在若干 tick 之后的 id 空间重叠，且每次重启都换一个值。IdGeneratorOptions.WorkerId 默认就是 0，
        // 所以直接调 IdHelper.SetIdGenerator(new IdGeneratorOptions()) 的脚本会静默拿到这种机器码 —— 在这里拒绝，
        // 与配置路径的 IdGenerationOptionsValidator 同一判据（那条守不住不经配置的公开 API 调用方）。
        var maxWorkerIdNumber = Math.Pow(2, options.WorkerIdBitLength) - 1;
        if (options.WorkerId < 1 || options.WorkerId > maxWorkerIdNumber)
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
