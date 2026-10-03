namespace Tnzi.AI.Cli.Metadata;

/// <summary>
/// PATH 上找不到 provider 的可执行文件。
/// </summary>
/// <remarks>
/// 单独成类而不是抛通用异常：调度层据此把失败归入
/// <see cref="CliRunFailureReason.ExecutableNotFound"/> —— 分类必须在**做出判断的那个分支**
/// 确定，而不是事后从错误文案反推。
/// </remarks>
public class CliExecutableNotFoundException : InfrastructureException
{
    /// <summary>没找到的路径。</summary>
    public string ExecutablePath { get; }

    /// <summary>初始化异常。</summary>
    public CliExecutableNotFoundException(string executablePath)
        : base("CliAgent", $"External agent executable was not found at '{executablePath}'.", isRetryable: false)
    {
        ExecutablePath = executablePath;
    }
}

/// <summary>
/// 子进程启动失败（权限、格式、资源等）。
/// </summary>
public class CliProcessLaunchException : InfrastructureException
{
    /// <summary>尝试启动的路径。</summary>
    public string ExecutablePath { get; }

    /// <summary>初始化异常。</summary>
    public CliProcessLaunchException(string executablePath, Exception innerException)
        : base("CliAgent", $"Failed to launch external agent process '{executablePath}'.", isRetryable: true, innerException)
    {
        ExecutablePath = executablePath;
    }
}

/// <summary>
/// provider 的部署配置要求了一项它不支持的能力（例如对不能重定向配置目录的 CLI 要求专用配置目录）。
/// </summary>
/// <remarks>
/// 启动期校验器已经拦过一次；运行期再抛是因为配置可以热更新。宁可让运行失败，
/// 也不以「未隔离」的状态悄悄跑起来 —— 部署方选了隔离，就是不接受不隔离。
/// </remarks>
public class CliProviderConfigurationException : InfrastructureException
{
    /// <summary>provider 键。</summary>
    public string ProviderKey { get; }

    /// <summary>初始化异常。</summary>
    public CliProviderConfigurationException(string providerKey, string message)
        : base("CliAgent", message, isRetryable: false)
    {
        ProviderKey = providerKey;
    }
}

/// <summary>
/// provider 声明的协议在本版本没有适配器实现。
/// </summary>
public class CliProtocolNotImplementedException : InfrastructureException
{
    /// <summary>无实现的协议族。</summary>
    public CliAgentProtocol Protocol { get; }

    /// <summary>初始化异常。</summary>
    public CliProtocolNotImplementedException(CliAgentProtocol protocol)
        : base("CliAgent", $"No protocol adapter is implemented for '{protocol}' in this version.", isRetryable: false)
    {
        Protocol = protocol;
    }
}
