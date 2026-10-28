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

    /// <summary>以明确的原因初始化异常（启动前就拒绝、不可重试）。</summary>
    public CliProcessLaunchException(string executablePath, string reason)
        : base("CliAgent", $"Refused to launch external agent process '{executablePath}': {reason}", isRetryable: false)
    {
        ExecutablePath = executablePath;
    }
}

/// <summary>
/// 执行宿主给不出这个作用域的目标：对应的远程端没连上、容器没起来，或宿主不服务这种作用域。
/// </summary>
/// <remarks>
/// 运行以 <see cref="CliRunFailureReason.LaunchFailed"/> 结束（消息原样给出），运行时探测跳过这一轮。
/// </remarks>
public class CliHostUnavailableException : InfrastructureException
{
    /// <summary>初始化异常。</summary>
    /// <param name="message">面向使用者的说明（英文），说清是哪个目标、为什么不可用。</param>
    /// <param name="isRetryable">稍后再试是否可能成功（远程端掉线 = true；宿主不服务这种作用域 = false）。</param>
    /// <param name="innerException">底层原因。</param>
    public CliHostUnavailableException(string message, bool isRetryable = true, Exception? innerException = null)
        : base("CliAgent", message, isRetryable, innerException)
    {
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
/// 工作目录正被另一次运行（另一条线程）占用：身份哨兵能解析，且归属他人。
/// </summary>
/// <remarks>
/// 单独成类，调度层才能把它归入 <see cref="CliRunFailureReason.WorkspacePrepareFailed"/>（此前是通用异常，落成 <c>Unknown</c>），
/// 并据 <see cref="OwnerRunId"/> 回查占用者：它已经结束时，标记是崩溃留下的残留，可以回收。
/// </remarks>
public class CliWorkspaceClaimedException : InfrastructureException
{
    /// <summary>标记里记录的占用者运行。</summary>
    public Guid OwnerRunId { get; }

    /// <summary>被占用的目录。</summary>
    public string Directory { get; }

    /// <summary>初始化异常。</summary>
    public CliWorkspaceClaimedException(string directory, Guid ownerRunId)
        : base("CliAgent", $"Workspace at '{directory}' is already claimed by run {ownerRunId}.", isRetryable: false)
    {
        Directory = directory;
        OwnerRunId = ownerRunId;
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
