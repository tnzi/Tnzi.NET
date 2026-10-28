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
    // Kubernetes 给 Deployment / ReplicaSet 的 Pod 起名 <名称>-<模板哈希>-<5 位随机>，两段都取自同一个去掉元音与易混字符的字母表
    private static readonly Regex KubernetesGeneratedPodName = new(
        "-[bcdfghjklmnpqrstvwxz2456789]{6,10}-[bcdfghjklmnpqrstvwxz2456789]{5}$",
        RegexOptions.CultureInvariant);

    // Docker 默认以容器 ID（12 位短 ID 或 64 位完整 ID）作主机名
    private static readonly Regex ContainerIdHostname = new(
        "^(?:[0-9a-f]{12}|[0-9a-f]{64})$",
        RegexOptions.CultureInvariant);

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

        if (!options.WorkerIdFromHostname
            || IsGeneratedHostname(machineName)
            || !TryParseHostnameOrdinal(machineName, out var ordinal))
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
    /// 主机名是不是平台随机生成、末尾不带稳定序号的形态：K8s Deployment / ReplicaSet 的 Pod 名
    /// （<c>api-6d4cf56db6-x7k27</c>）、Docker 以容器 ID 作的主机名（<c>3f2a9c1b7d84</c>）。
    /// </summary>
    /// <remarks>
    /// 这类主机名碰巧以数字结尾时，末尾数字是随机字符而不是序号：<c>api-6d4cf56db6-q7</c> 与 <c>api-6d4cf56db6-k7</c>
    /// 都会派生出机器码 8，两个副本在同一毫秒生成相同的 long 主键。末尾是字母的那些本来就按未配置处理，
    /// 于是同一个部署有的副本拒绝启动、有的副本静默撞号。按未配置处理之后，整组副本行为一致。
    /// </remarks>
    private static bool IsGeneratedHostname(string? machineName)
    {
        return !string.IsNullOrEmpty(machineName)
            && (KubernetesGeneratedPodName.IsMatch(machineName) || ContainerIdHostname.IsMatch(machineName));
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
