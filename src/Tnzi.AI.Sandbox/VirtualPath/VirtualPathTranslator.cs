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
        var physicalPath = Path.GetFullPath(Path.Combine(threadDir, relativePart));

        // Resolved path must be within thread directory (case-insensitive on Windows)
        var normalizedThreadDir = Path.GetFullPath(threadDir);
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

        var threadDir = Path.GetFullPath(GetThreadDirectory(threadId));
        var normalizedPhysical = Path.GetFullPath(physicalPath);

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
