namespace Tnzi.AI.Sandbox.Tools;

public class SandboxTools
{
    private readonly IVirtualPathTranslator _translator;
    private readonly ILogger<SandboxTools> _logger;

    public SandboxTools(IVirtualPathTranslator translator, ILogger<SandboxTools> logger)
    {
        _translator = Check.NotNull(translator);
        _logger = Check.NotNull(logger);
    }

    [Description("Execute a bash command in the sandbox environment")]
    public async Task<object> BashAsync(ISandbox sandbox, Guid threadId, string command)
    {
        Check.NotNullOrWhiteSpace(command);
        var translatedCommand = TranslatePathsInCommand(command, threadId);
        var result = await sandbox.ExecuteCommandAsync(translatedCommand);
        return new { stdout = result.Output, stderr = result.Error, exit_code = result.ExitCode };
    }

    [Description("List contents of a directory in the sandbox")]
    public async Task<object> ListDirectoryAsync(ISandbox sandbox, Guid threadId, string path, int maxDepth = 2)
    {
        var physicalPath = _translator.ToPhysical(path, threadId);
        var entries = await sandbox.ListDirectoryAsync(physicalPath, maxDepth);
        return new
        {
            path,
            entries = entries.Select(e => new
            {
                name = e.Name,
                type = e.IsDirectory ? "directory" : "file",
                size = e.Size
            }).ToArray()
        };
    }

    [Description("Read the contents of a file in the sandbox")]
    public async Task<object> ReadFileAsync(ISandbox sandbox, Guid threadId, string path, int? offset = null, int? limit = null)
    {
        var physicalPath = _translator.ToPhysical(path, threadId);
        var content = await sandbox.ReadFileAsync(physicalPath);

        if (offset.HasValue || limit.HasValue)
        {
            var lines = content.Split('\n');
            var start = Math.Max(0, (offset ?? 1) - 1);
            var count = limit ?? lines.Length;
            content = string.Join('\n', lines.Skip(start).Take(count));
        }

        return new { path, content };
    }

    [Description("Write content to a file in the sandbox. Creates directories if needed.")]
    public async Task<object> WriteFileAsync(ISandbox sandbox, Guid threadId, string path, string content, bool append = false)
    {
        var physicalPath = _translator.ToPhysical(path, threadId);
        await sandbox.WriteFileAsync(physicalPath, content, append);
        return new { success = true, path };
    }

    [Description("Replace a string in a file in the sandbox")]
    public async Task<object> StrReplaceAsync(ISandbox sandbox, Guid threadId, string path, string oldString, string newString)
    {
        var physicalPath = _translator.ToPhysical(path, threadId);
        var content = await sandbox.ReadFileAsync(physicalPath);

        if (!content.Contains(oldString))
            return new { success = false, error = $"String '{oldString}' not found in file" };

        var updated = content.Replace(oldString, newString);
        await sandbox.WriteFileAsync(physicalPath, updated);
        return new { success = true, path, replacements = content.Split(oldString).Length - 1 };
    }

    private string TranslatePathsInCommand(string command, Guid threadId)
    {
        var threadDir = _translator.GetThreadDirectory(threadId);
        return command
            .Replace("/mnt/workspace", Path.Combine(threadDir, "workspace"))
            .Replace("/mnt/uploads", Path.Combine(threadDir, "uploads"))
            .Replace("/mnt/outputs", Path.Combine(threadDir, "outputs"))
            .Replace("/mnt/skills", Path.Combine(threadDir, "skills"));
    }
}
