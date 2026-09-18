namespace Tnzi.Data.Snows;

/// <summary>
/// 机器码的来源。
/// </summary>
public enum WorkerIdSource
{
    /// <summary>
    /// 配置显式给出（<c>IdGeneration:WorkerId</c>）。
    /// </summary>
    Configuration,

    /// <summary>
    /// 由主机名末尾的序号派生（<c>IdGeneration:WorkerIdFromHostname</c>）。
    /// </summary>
    Hostname,
}

/// <summary>
/// 机器码解析结果。
/// </summary>
/// <param name="WorkerId">解析出的机器码</param>
/// <param name="Source">来源</param>
public sealed record WorkerIdResolution(ushort WorkerId, WorkerIdSource Source);

/// <summary>
/// 按 <see cref="IdGenerationOptions"/> 解析本进程的雪花机器码。
/// </summary>
public static class WorkerIdResolver
{
    /// <summary>
    /// 解析机器码：显式配置优先，其次按主机名序号派生；两者都没有返回 <c>null</c>（由调用方按环境决定失败方向）。
    /// </summary>
    /// <param name="options">配置</param>
    /// <param name="machineName">主机名（通常是 <see cref="Environment.MachineName"/>）</param>
    /// <exception cref="ConfigurationException">主机名派生出的机器码超出 <see cref="IdGenerationOptions.WorkerIdBitLength"/> 能表达的范围</exception>
    public static WorkerIdResolution? Resolve(IdGenerationOptions options, string machineName)
    {
        Check.NotNull(options);

        if (options.WorkerId is { } configured)
        {
            return new WorkerIdResolution(configured, WorkerIdSource.Configuration);
        }

        if (!options.WorkerIdFromHostname || !TryParseHostnameOrdinal(machineName, out var ordinal))
        {
            return null;
        }

        // StatefulSet 序号从 0 起，而 WorkerId 0 不是合法机器码（生成器拒绝），故 +1
        var workerId = ordinal + 1;
        if (workerId > options.MaxWorkerId)
        {
            throw new ConfigurationException(
                "IdGeneration:WorkerIdFromHostname",
                $"Hostname '{machineName}' yields WorkerId {workerId}, which exceeds the maximum {options.MaxWorkerId} " +
                $"representable with IdGeneration:WorkerIdBitLength={options.WorkerIdBitLength}. Raise WorkerIdBitLength " +
                "(keeping WorkerIdBitLength + SeqBitLength <= 22) or set IdGeneration:WorkerId explicitly.");
        }

        return new WorkerIdResolution((ushort)workerId, WorkerIdSource.Hostname);
    }

    /// <summary>
    /// 取主机名末尾的连续数字作为序号（<c>api-2</c> → 2，<c>web12</c> → 12）；末尾不是数字返回 false。
    /// </summary>
    public static bool TryParseHostnameOrdinal(string? machineName, out int ordinal)
    {
        ordinal = 0;
        if (string.IsNullOrEmpty(machineName))
        {
            return false;
        }

        var end = machineName.Length;
        var start = end;
        while (start > 0 && char.IsAsciiDigit(machineName[start - 1]))
        {
            start--;
        }

        return start < end && int.TryParse(machineName.AsSpan(start, end - start), out ordinal);
    }
}
