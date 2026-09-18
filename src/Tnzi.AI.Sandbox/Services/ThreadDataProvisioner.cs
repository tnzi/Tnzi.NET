namespace Tnzi.AI.Sandbox.Services;

/// <summary>
/// <see cref="IThreadDataProvisioner"/> 默认实现：建线程目录、按配置预建四个子目录、把每个已加载技能的资源文件
/// 复制进 <c>{ThreadDir}/skills/</c>。
/// </summary>
/// <remarks>
/// <para>
/// 线程根目录<b>一律</b>创建：Local provider 把它当 bash 的工作目录（不存在则进程起不来），Docker provider 把它当
/// bind 挂载源（不存在时由 Docker 以 root 建出来）。四个子目录只在 <c>LazyDirectoryCreation=false</c> 时预建，
/// 否则由沙箱在首次写入时按需创建。
/// </para>
/// <para>
/// ★ <b>skills/ 必须是线程目录里的真目录，绝不能是指向线程目录之外的链接。</b>
/// 2026-05-22 起这里曾优先把 <c>skills/</c> 软链到启动时提取好的共享根
/// <c>{DataRoot}/_skills</c>（省掉逐线程复制的 IO）。2026-09-04 围栏改为<b>解析符号链接之后</b>
/// 再判定是否在线程目录内（不解析的话 agent 一条 <c>ln -s /etc</c> 就能读宿主文件），
/// 于是这条链接本身也被判成越界：任何能建链接的宿主上，<c>read_file</c> / <c>ls</c>
/// 在 <c>/mnt/skills</c> 上一律报 "Path traversal detected"，而 Windows 无开发者模式
/// 走的是复制回退，测试全绿。两个机制各自都对，合起来把技能目录整个关掉了。
/// </para>
/// <para>
/// 刻意选择<b>取消链接、一律复制</b>，而不是给围栏开一个"共享根也算界内"的口子：
/// 那个口子要在翻译器、本地沙箱、Docker 绑定三处各开一次（容器里根本看不见宿主的链接目标），
/// 而每处都是围栏本身。逐线程复制的代价是每个真的用到沙箱的线程一次性写入内置技能的资源文件
/// （现约 2 MB / 139 个文件）；2026-09-14 起复制推迟到沙箱首次被用到，不再是每个线程的固定开销。
/// </para>
/// <para>
/// 经线程目录里的哨兵 <see cref="SkillsWiredMarker"/> 幂等，但哨兵本身不是证明：
/// 2026-05-22 至 2026-09-12 之间接线的线程带着哨兵<i>和</i>一条指向已删共享根的 <c>skills/</c> 链接，
/// 只信哨兵会让恰好这些线程永远被围栏拦在外面（运维删掉残留的 <c>_skills/</c> 之后还是一条悬空链接）。
/// 所以哨兵只在 <c>skills/</c> 是真目录时才算数；链接一律 unlink（绝不跟进）后重新复制。
/// </para>
/// </remarks>
public sealed class ThreadDataProvisioner : IThreadDataProvisioner
{
    /// <summary>
    /// 技能资源接线完成的哨兵文件，写在<b>线程目录</b>里（不在 skills/ 子目录里）。
    /// 只在整包复制成功后写，半途失败下次进入重试。
    /// </summary>
    public const string SkillsWiredMarker = ".skills_wired";

    private readonly IOptions<SandboxModuleOptions> _options;
    private readonly IVirtualPathTranslator _translator;
    private readonly ILogger<ThreadDataProvisioner> _logger;
    private readonly ISkillStore? _skillStore;

    public ThreadDataProvisioner(
        IOptions<SandboxModuleOptions> options,
        IVirtualPathTranslator translator,
        ILogger<ThreadDataProvisioner> logger,
        ISkillStore? skillStore = null)
    {
        _options = Check.NotNull(options);
        _translator = Check.NotNull(translator);
        _logger = Check.NotNull(logger);
        _skillStore = skillStore;
    }

    /// <inheritdoc />
    public async Task ProvisionAsync(Guid threadId, ThreadDataState state, CancellationToken ct = default)
    {
        Check.NotNull(state);

        Directory.CreateDirectory(state.ThreadDirectory);

        if (!_options.Value.LazyDirectoryCreation)
        {
            _translator.EnsureThreadDirectories(threadId);
            _logger.LogDebug("Created thread directories for {ThreadId}", threadId);
        }

        await WireSkillResourcesAsync(state, ct);
    }

    /// <summary>
    /// 把每个已加载技能的资源文件复制进 <see cref="ThreadDataState.SkillsPath"/>。
    /// </summary>
    private async Task WireSkillResourcesAsync(ThreadDataState state, CancellationToken ct)
    {
        if (_skillStore == null) return;

        var skillsPath = state.SkillsPath;
        var marker = Path.Combine(state.ThreadDirectory, SkillsWiredMarker);
        if (File.Exists(marker) && IsRealDirectory(skillsPath)) return;

        if (IsLink(skillsPath))
        {
            RemoveLink(skillsPath);
            _logger.LogInformation("Replaced legacy skills symlink at {Path} with a per-thread copy", skillsPath);
        }

        if (await CopyAllSkillResourcesAsync(skillsPath, ct))
        {
            await File.WriteAllTextAsync(marker, $"copied at {DateTime.UtcNow:O}", ct);
        }
    }

    private static bool IsRealDirectory(string path) => Directory.Exists(path) && !IsLink(path);

    /// <summary>
    /// True when <paramref name="path"/> is itself a link (symlink or junction), dangling or not.
    /// <see cref="File.GetAttributes(string)"/> reads the link entry rather than its target.
    /// </summary>
    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    /// <summary>
    /// Removes the link entry only, never the directory it points to. On Windows a directory
    /// link is removed by the directory API; on Unix a symlink is a file entry, and
    /// <see cref="Directory.Delete(string)"/> would refuse a dangling one.
    /// </summary>
    private static void RemoveLink(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.Delete(path);
        else File.Delete(path);
    }

    /// <summary>
    /// Copies every loaded skill's resource files into <paramref name="skillsPath"/>.
    /// Returns <c>true</c> on success; the caller writes the completion marker.
    /// </summary>
    private async Task<bool> CopyAllSkillResourcesAsync(string skillsPath, CancellationToken ct)
    {
        try
        {
            var skills = await _skillStore!.GetAllAsync(ct);
            var extractedCount = 0;

            foreach (var skill in skills)
            {
                if (skill.Resources.Count == 0) continue;

                var skillDir = Path.Combine(skillsPath, skill.Slug);
                foreach (var (relativePath, content) in skill.Resources)
                {
                    var filePath = Path.Combine(skillDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(filePath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    await File.WriteAllTextAsync(filePath, content, ct);
                    extractedCount++;
                }
            }

            Directory.CreateDirectory(skillsPath);

            if (extractedCount > 0)
                _logger.LogDebug("Copied {Count} skill resource files to {Path}", extractedCount, skillsPath);

            return true;
        }
        catch (Exception ex)
        {
            // Failed extraction: remove the partial directory so the next request retries.
            try { if (Directory.Exists(skillsPath)) Directory.Delete(skillsPath, recursive: true); }
            catch { /* best effort cleanup */ }

            _logger.LogWarning(ex, "Failed to extract skill resources to {Path}", skillsPath);
            return false;
        }
    }
}
