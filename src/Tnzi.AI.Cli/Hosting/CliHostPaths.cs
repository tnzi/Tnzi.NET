namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 一个执行目标的路径语法：拼接、取父目录与文件名、规范化、比较。
/// </summary>
/// <remarks>
/// 工作区的每一条路径都由框架拼出来、交给目标去用，所以拼法必须是<b>目标</b>的拼法而不是 API 进程所在系统的：
/// Windows 上的 <see cref="Path.Combine(string, string)"/> 会把 <c>/home/agent/ws</c> 与 <c>workdir</c> 拼成
/// <c>/home/agent/ws\workdir</c>，<see cref="Path.GetFullPath(string)"/> 会把它变成 <c>C:\home\agent\ws</c>。
/// 本机目标用 <see cref="Native"/>，Linux 容器之类的远程目标用 <see cref="Posix"/>。
/// </remarks>
public abstract class CliHostPaths
{
    /// <summary>API 进程所在系统的路径语法（<see cref="Path"/>）。</summary>
    public static CliHostPaths Native { get; } = new NativePaths();

    /// <summary>POSIX 路径语法：<c>/</c> 分隔、区分大小写、以 <c>/</c> 开头才是绝对路径。</summary>
    public static CliHostPaths Posix { get; } = new PosixPaths();

    /// <summary>目录分隔符。</summary>
    public abstract char Separator { get; }

    /// <summary>路径比较方式。</summary>
    public abstract StringComparison Comparison { get; }

    /// <summary>
    /// 在 <paramref name="basePath"/> 下拼一个相对路径；<paramref name="relativePath"/> 里可以用 <c>/</c> 分多级。
    /// <paramref name="relativePath"/> 本身是绝对路径时返回它（与 <see cref="Path.Combine(string, string)"/> 相同）。
    /// </summary>
    public abstract string Combine(string basePath, string relativePath);

    /// <summary>依次拼接多段。</summary>
    public string Combine(string basePath, params string[] relativePaths)
    {
        Check.NotNull(relativePaths);
        return relativePaths.Aggregate(basePath, Combine);
    }

    /// <summary>父目录；已是根时为 null。</summary>
    public abstract string? GetDirectoryName(string path);

    /// <summary>最后一段的名字。</summary>
    public abstract string GetFileName(string path);

    /// <summary>规范化成不带 <c>.</c> / <c>..</c>、不以分隔符结尾的绝对路径（根除外）。</summary>
    /// <exception cref="ArgumentException">路径不是绝对路径且这种语法无从解析它。</exception>
    public abstract string GetFullPath(string path);

    /// <summary>两条路径是否指向同一处（规范化后按 <see cref="Comparison"/> 比较）。</summary>
    public bool AreSame(string path, string? other)
        => !string.IsNullOrWhiteSpace(other)
           && string.Equals(GetFullPath(path), GetFullPath(other), Comparison);

    /// <summary><paramref name="path"/> 是否严格位于 <paramref name="root"/> 之下。</summary>
    public bool IsUnder(string path, string root)
    {
        var normalizedRoot = GetFullPath(root);
        var prefix = normalizedRoot.EndsWith(Separator) ? normalizedRoot : normalizedRoot + Separator;
        var normalizedPath = GetFullPath(path);
        return normalizedPath.Length > prefix.Length && normalizedPath.StartsWith(prefix, Comparison);
    }

    private sealed class NativePaths : CliHostPaths
    {
        public override char Separator => Path.DirectorySeparatorChar;

        public override StringComparison Comparison
            => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public override string Combine(string basePath, string relativePath)
            => Path.Combine(basePath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        public override string? GetDirectoryName(string path) => Path.GetDirectoryName(path);

        public override string GetFileName(string path) => Path.GetFileName(path);

        public override string GetFullPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private sealed class PosixPaths : CliHostPaths
    {
        public override char Separator => '/';

        public override StringComparison Comparison => StringComparison.Ordinal;

        public override string Combine(string basePath, string relativePath)
        {
            Check.NotNull(basePath);
            Check.NotNull(relativePath);

            if (relativePath.StartsWith('/') || basePath.Length == 0)
            {
                return relativePath;
            }

            return relativePath.Length == 0 ? basePath : basePath.TrimEnd('/') + "/" + relativePath;
        }

        public override string? GetDirectoryName(string path)
        {
            Check.NotNull(path);

            var trimmed = path.Length > 1 ? path.TrimEnd('/') : path;
            if (trimmed == "/")
            {
                return null;
            }

            var index = trimmed.LastIndexOf('/');
            return index switch
            {
                < 0 => string.Empty,
                0 => "/",
                _ => trimmed[..index]
            };
        }

        public override string GetFileName(string path)
        {
            Check.NotNull(path);
            var index = path.LastIndexOf('/');
            return index < 0 ? path : path[(index + 1)..];
        }

        public override string GetFullPath(string path)
        {
            Check.NotNullOrWhiteSpace(path);
            if (!path.StartsWith('/'))
            {
                throw new ArgumentException(
                    $"'{path}' is not an absolute path on the execution target; paths on a POSIX target must start with '/'.", nameof(path));
            }

            var segments = new List<string>();
            foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                switch (segment)
                {
                    case ".":
                        continue;
                    case "..":
                        if (segments.Count > 0)
                        {
                            segments.RemoveAt(segments.Count - 1);
                        }

                        continue;
                    default:
                        segments.Add(segment);
                        break;
                }
            }

            return "/" + string.Join('/', segments);
        }
    }
}
