namespace Tnzi.AI.Cli.Workspace;

/// <summary>
/// CLI 在它的配置目录里为一个会话留下的记录：在哪里、怎么删。
/// </summary>
/// <remarks>
/// <para>
/// 只在 <see cref="CliUserConfigIsolation.ProvidedConfigDirectory"/> 下、启动档案给出了配置目录与项目目录名时找得到：
/// 别的档位要么随工作区一起删（<see cref="CliUserConfigIsolation.PerWorkspaceConfigDirectory"/>），
/// 要么记录按工作目录的路径命名、框架不去猜 CLI 的换算规则。
/// </para>
/// <para>
/// 会话 id 来自 CLI 的输出，项目目录名来自消费方：两者只接受字母、数字、<c>-</c>、<c>_</c>、<c>.</c>（且不能是 <c>.</c> / <c>..</c>），
/// 否则一个 <c>../</c> 就能把「删这个会话的记录」变成「删配置目录外的任意路径」。
/// </para>
/// </remarks>
internal static class CliSessionStore
{
    private const string ProjectPlaceholder = "{project}";
    private const string SessionPlaceholder = "{session}";

    /// <summary>这个会话的记录路径（执行目标上的）；找不到时为空。</summary>
    public static IReadOnlyList<string> Locate(
        CliProviderDescriptor provider, CliLaunchProfile profile, CliHostPaths paths, string sessionId)
    {
        Check.NotNull(provider);
        Check.NotNull(profile);
        Check.NotNull(paths);

        if (provider.UserConfigIsolation != CliUserConfigIsolation.ProvidedConfigDirectory
            || provider.SessionStorePaths.Count == 0
            || string.IsNullOrWhiteSpace(profile.ConfigDirectory)
            || !IsSafeSegment(profile.ProjectDirectoryName)
            || !IsSafeSegment(sessionId))
        {
            return [];
        }

        return provider.SessionStorePaths
            .Select(template => paths.Combine(
                profile.ConfigDirectory,
                template.Replace(ProjectPlaceholder, profile.ProjectDirectoryName, StringComparison.Ordinal)
                    .Replace(SessionPlaceholder, sessionId, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>删掉这些路径（文件或目录，不存在的跳过），返回删掉了几个。</summary>
    public static async Task<int> DeleteAsync(ICliHostFileSystem fileSystem, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        Check.NotNull(fileSystem);
        Check.NotNull(paths);

        var deleted = 0;
        foreach (var path in paths)
        {
            if (await fileSystem.FileExistsAsync(path, cancellationToken))
            {
                await fileSystem.DeleteFileAsync(path, cancellationToken);
                deleted++;
            }
            else if (await fileSystem.DirectoryExistsAsync(path, cancellationToken))
            {
                await fileSystem.DeleteDirectoryAsync(path, recursive: true, cancellationToken);
                deleted++;
            }
        }

        return deleted;
    }

    private static bool IsSafeSegment([NotNullWhen(true)] string? segment)
        => !string.IsNullOrWhiteSpace(segment)
           && segment is not ("." or "..")
           && segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
