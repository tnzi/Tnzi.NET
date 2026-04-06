namespace Tnzi.AI.Sandbox.Options;

public class SandboxModuleOptions
{
    public string Provider { get; set; } = "local";
    public string DataRoot { get; set; } = ".tnzi-ai/threads";
    public bool Enabled { get; set; } = true;
    public bool LazyDirectoryCreation { get; set; } = true;
    public LocalSandboxOptions Local { get; set; } = new();
    public DockerSandboxOptions Docker { get; set; } = new();
    public KubernetesSandboxOptions Kubernetes { get; set; } = new();
}

public class LocalSandboxOptions
{
    public List<string> AllowedDirectories { get; set; } = ["."];
    public List<string> DeniedCommands { get; set; } = ["rm -rf /", "format c:", "chmod 777 /", "mkfs"];
    public List<string> DeniedPatterns { get; set; } = [".env", "*.key", "*.pem", "credentials*"];
    public List<string> EnvironmentBlacklist { get; set; } =
        ["API_KEY", "SECRET_KEY", "ACCESS_TOKEN", "PRIVATE_KEY", "OPENAI_API_KEY", "ANTHROPIC_API_KEY"];
}

public class DockerSandboxOptions
{
    /// <summary>
    /// Docker daemon host URI. Defaults to platform-appropriate socket.
    /// Linux: unix:///var/run/docker.sock, Windows: npipe:////./pipe/docker_engine
    /// </summary>
    public string DockerHost { get; set; } = OperatingSystem.IsWindows()
        ? "npipe:////./pipe/docker_engine"
        : "unix:///var/run/docker.sock";

    /// <summary>
    /// Default container image for sandbox execution
    /// </summary>
    public string Image { get; set; } = "mcr.microsoft.com/dotnet/sdk:10.0";

    /// <summary>
    /// Maximum number of concurrent containers
    /// </summary>
    public int MaxContainers { get; set; } = 5;

    /// <summary>
    /// Container idle timeout in seconds before auto-cleanup
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Container memory limit in MB
    /// </summary>
    public int MemoryLimitMb { get; set; } = 512;

    /// <summary>
    /// Container CPU limit (1.0 = one full CPU core)
    /// </summary>
    public double CpuLimit { get; set; } = 1.0;

    /// <summary>
    /// Enable automatic container cleanup on disposal
    /// </summary>
    public bool AutoRemove { get; set; } = true;
}

public class KubernetesSandboxOptions
{
    public string Namespace { get; set; } = "tnzi-sandbox";
    public string Image { get; set; } = "mcr.microsoft.com/dotnet/sdk:10.0";
    public string? CpuRequest { get; set; }
    public string? MemoryRequest { get; set; }
    public string? CpuLimit { get; set; }
    public string? MemoryLimit { get; set; }
}
