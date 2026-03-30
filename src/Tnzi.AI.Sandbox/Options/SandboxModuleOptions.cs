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
    public string Image { get; set; } = "mcr.microsoft.com/dotnet/sdk:10.0";
    public int IdleTimeoutSeconds { get; set; } = 300;
    public string? CpuLimit { get; set; }
    public string? MemoryLimit { get; set; }
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
