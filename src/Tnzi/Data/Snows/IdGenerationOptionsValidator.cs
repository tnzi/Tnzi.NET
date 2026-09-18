namespace Tnzi.Data.Snows;

/// <summary>
/// <see cref="IdGenerationOptions"/> 校验器。
/// </summary>
/// <remarks>
/// 「未配置 WorkerId」不是校验错误：失败方向由环境决定（见 <see cref="IdGenerationOptions"/>），
/// 校验器只管给出的值在不在算法允许的范围里。WorkerId 0 在这里就拒绝：
/// <c>DefaultIdGenerator</c> 同样拒绝 0（此前 <c>SnowWorkerM1</c> 把 0 替换成当前毫秒数，每次启动机器码都不同且溢出位长），
/// 这里提前拒绝是为了让错误信息指着配置键而不是生成器内部。
/// </remarks>
public class IdGenerationOptionsValidator : OptionsValidatorBase<IdGenerationOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(IdGenerationOptions options, List<string> errors)
    {
        if (options.WorkerIdBitLength is < 1 or > 21)
        {
            errors.Add("WorkerIdBitLength must be between 1 and 21");
        }

        if (options.SeqBitLength is < 2 or > 21)
        {
            errors.Add("SeqBitLength must be between 2 and 21");
        }

        if (options.WorkerIdBitLength + options.SeqBitLength > 22)
        {
            errors.Add("WorkerIdBitLength + SeqBitLength must not exceed 22");
        }

        if (options.WorkerId is { } workerId && (workerId < 1 || workerId > options.MaxWorkerId))
        {
            errors.Add($"WorkerId must be between 1 and {options.MaxWorkerId} for WorkerIdBitLength={options.WorkerIdBitLength}");
        }
    }
}
