namespace Tnzi.AI.Sandbox.Options;

public class SandboxModuleOptionsValidator : OptionsValidatorBase<SandboxModuleOptions>
{
    private static readonly HashSet<string> SupportedProviders =
        new(StringComparer.OrdinalIgnoreCase) { "local", "docker", "kubernetes" };

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly IHostEnvironment? _hostEnvironment;

    public SandboxModuleOptionsValidator(IHostEnvironment? hostEnvironment = null, ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
    {
        _hostEnvironment = hostEnvironment;
    }

    protected override void ValidateOptions(SandboxModuleOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
            errors.Add("Provider must not be empty");
        else if (!SupportedProviders.Contains(options.Provider))
            errors.Add($"Provider '{options.Provider}' is not supported. Valid values: local, docker, kubernetes.");

        if (string.IsNullOrWhiteSpace(options.DataRoot))
        {
            // 默认值只在宿主解析不出用户数据目录时才为空（IIS 未加载用户配置文件、没有 HOME 的服务账号）。
            // 刻意不退回相对路径：那会把线程数据写回部署目录，正是默认值刚离开的地方。
            errors.Add(
                "DataRoot must not be empty. No per-user data directory (LocalApplicationData) is available on this host, " +
                "so the default could not be resolved; set AI:Sandbox:DataRoot to an absolute path outside the application directory.");
        }
    }

    /// <summary>
    /// 配到内容根之下的 DataRoot 只警告不拒绝：那是消费方的显式选择（开发期放在项目目录里很常见），
    /// 但把发布目录当作整体同步到生产主机的部署会把每个线程的工作区一并同步出去，值得在启动时点名。
    /// </summary>
    protected override void CollectWarnings(SandboxModuleOptions options, List<string> warnings)
    {
        if (_hostEnvironment is null || string.IsNullOrWhiteSpace(options.DataRoot)) return;
        if (!IsUnderContentRoot(options.DataRoot, _hostEnvironment.ContentRootPath, out var resolvedDataRoot, out var contentRoot)) return;

        warnings.Add(
            $"DataRoot '{resolvedDataRoot}' lies inside the application content root '{contentRoot}'. " +
            "Thread workspaces, uploads and per-thread skill copies will be written into the deployed application directory; " +
            "deployments that ship or sync the publish directory as a unit will replicate all of it. " +
            $"Set AI:Sandbox:DataRoot to a location outside the content root (the default is '{SandboxModuleOptions.DefaultDataRoot}').");
    }

    /// <summary>
    /// 与 <c>VirtualPathTranslator</c> 同一口径解析 DataRoot（相对路径按进程当前目录），再和内容根比前缀。
    /// </summary>
    public static bool IsUnderContentRoot(string dataRoot, string? contentRootPath, out string resolvedDataRoot, out string contentRoot)
    {
        resolvedDataRoot = dataRoot;
        contentRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(contentRootPath)) return false;

        try
        {
            resolvedDataRoot = Path.GetFullPath(dataRoot);
            contentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRootPath));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // 路径本身不合法：这里只负责警告，让 VirtualPathTranslator 在解析时以它自己的异常报出来。
            return false;
        }

        return string.Equals(resolvedDataRoot, contentRoot, PathComparison)
            || resolvedDataRoot.StartsWith(contentRoot + Path.DirectorySeparatorChar, PathComparison)
            || resolvedDataRoot.StartsWith(contentRoot + Path.AltDirectorySeparatorChar, PathComparison);
    }
}
