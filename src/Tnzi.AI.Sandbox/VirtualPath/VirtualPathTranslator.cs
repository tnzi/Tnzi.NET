namespace Tnzi.AI.Sandbox.VirtualPath;

public class VirtualPathTranslator : IVirtualPathTranslator
{
    private const string VirtualPrefix = "/mnt/";
    private static readonly string[] ValidSubPaths = ["workspace", "uploads", "outputs", "skills"];
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly string _dataRoot;

    public VirtualPathTranslator(string dataRoot)
    {
        _dataRoot = Check.NotNullOrWhiteSpace(dataRoot);
    }

    public string ToPhysical(string virtualPath, Guid threadId)
    {
        Check.NotNullOrWhiteSpace(virtualPath);

        if (!virtualPath.StartsWith(VirtualPrefix, StringComparison.Ordinal))
            throw new SecurityException($"Invalid virtual path prefix: {virtualPath}");

        var relativePart = virtualPath[VirtualPrefix.Length..];
        var threadDir = GetThreadDirectory(threadId);

        // ★ 必须解析符号链接后再判定：Path.GetFullPath 只做字面规范化，
        // 于是 agent 在线程目录里放一个 `ln -s /etc x` 之后，/mnt/workspace/x/passwd
        // 字面上完全待在界内，实际却落到宿主的 /etc 上。
        var physicalPath = RealPathResolver.Resolve(Path.Combine(threadDir, relativePart));
        var normalizedThreadDir = RealPathResolver.Resolve(threadDir);

        if (!physicalPath.StartsWith(normalizedThreadDir + Path.DirectorySeparatorChar, PathComparison)
            && !string.Equals(physicalPath, normalizedThreadDir, PathComparison))
        {
            throw new SecurityException($"Path traversal detected: {virtualPath} resolves outside thread directory");
        }

        return physicalPath;
    }

    public string ToVirtual(string physicalPath, Guid threadId)
    {
        Check.NotNullOrWhiteSpace(physicalPath);

        var threadDir = RealPathResolver.Resolve(GetThreadDirectory(threadId));
        var normalizedPhysical = RealPathResolver.Resolve(physicalPath);

        if (!normalizedPhysical.StartsWith(threadDir + Path.DirectorySeparatorChar, PathComparison)
            && !string.Equals(normalizedPhysical, threadDir, PathComparison))
            throw new SecurityException($"Physical path is not within thread directory: {physicalPath}");

        var relative = Path.GetRelativePath(threadDir, normalizedPhysical);
        return VirtualPrefix + relative.Replace('\\', '/');
    }

    public bool IsValidVirtualPath(string virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath) || !virtualPath.StartsWith(VirtualPrefix, StringComparison.Ordinal))
            return false;

        var afterPrefix = virtualPath[VirtualPrefix.Length..];
        var firstSegment = afterPrefix.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstSegment is not null && ValidSubPaths.Contains(firstSegment, StringComparer.OrdinalIgnoreCase);
    }

    public string GetThreadDirectory(Guid threadId)
        => Path.Combine(_dataRoot, threadId.ToString("N"));

    public void EnsureThreadDirectories(Guid threadId)
    {
        var threadDir = GetThreadDirectory(threadId);
        foreach (var subPath in ValidSubPaths)
        {
            Directory.CreateDirectory(Path.Combine(threadDir, subPath));
        }
    }
}
