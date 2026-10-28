namespace Tnzi.AI.Sandbox.Services;

/// <summary>
/// <see cref="IThreadDataProvisioner"/> 默认实现：建线程目录、按配置预建四个子目录、把每个已加载技能的资源文件
/// 复制进 <c>{ThreadDir}/skills/</c>。
/// </summary>
/// <remarks>
/// <para>
/// 线程根目录<b>一律</b>创建：Local provider 把它当 bash 的工作目录（不存在则进程起不来），Docker provider 把它当
/// bind 挂载源（Docker provider 在建容器前自己再建一遍线程目录与四个子目录，免得缺失的挂载源由 Docker 以 root 建出来）。
/// 四个子目录在这里只在 <c>LazyDirectoryCreation=false</c> 时预建，
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
/// ★ 哨兵里记着复制时技能资源的指纹（全部 slug、相对路径与内容的 SHA-256）：技能仓库里的资源变了（改了脚本、加了文件、
/// 删了技能），下一次用到沙箱时整个 <c>skills/</c> 重新复制（先删后复制，删除不跟进链接），否则只读一下仓库缓存、算一次摘要。
/// 此前哨兵只记时间，一条线程一旦接过线就永远用那一刻的副本：管理员修好的脚本、删掉的技能在已有线程里一直是旧样子，
/// 而技能上下文按仓库现状列出的资源路径在副本里找不到。<c>skills/</c> 归框架所有（Docker 下容器对它只读），整体替换不丢 agent 的东西。
/// </para>
/// <para>
/// 经线程目录里的哨兵 <see cref="SkillsWiredMarker"/> 幂等，但哨兵本身不是证明：
/// 2026-05-22 至 2026-09-12 之间接线的线程带着哨兵<i>和</i>一条指向已删共享根的 <c>skills/</c> 链接，
/// 只信哨兵会让恰好这些线程永远被围栏拦在外面（运维删掉残留的 <c>_skills/</c> 之后还是一条悬空链接）。
/// 所以哨兵只在 <c>skills/</c> 是真目录时才算数；链接一律 unlink（绝不跟进）后重新复制。
/// </para>
/// <para>
/// ★ 同一线程的接线在进程内串行：同一线程的两次运行并发首次用到沙箱时，两边都没看到哨兵、同时往 <c>skills/</c>
/// 里写同名文件，一方撞上文件占用失败，按「半途失败清掉重来」把整个 <c>skills/</c> 递归删掉，而另一方随后写下哨兵
/// —— 此后哨兵在、目录却是空的，技能资源永久缺失。串行之后，后到的一方进锁再看哨兵就直接返回，失败清理也只会删掉
/// 自己写了一半的目录。多个进程共用同一个 DataRoot 时不在这把锁的范围内。
/// </para>
/// </remarks>
public sealed class ThreadDataProvisioner : IThreadDataProvisioner
{
    /// <summary>
    /// 技能资源接线完成的哨兵文件，写在<b>线程目录</b>里（不在 skills/ 子目录里）。
    /// 只在整包复制成功后写，半途失败下次进入重试。
    /// </summary>
    public const string SkillsWiredMarker = ".skills_wired";

    // 按线程目录分条带的进程内锁：条带数固定，不随线程数增长（按线程建锁会一直攒下去）；不同线程偶尔落到同一条带只是多等一次。
    private static readonly SemaphoreSlim[] WiringLocks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

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
    /// 把每个已加载技能的资源文件复制进 <see cref="ThreadDataState.SkillsPath"/>；已有副本与仓库现状一致时什么也不做。
    /// </summary>
    private async Task WireSkillResourcesAsync(ThreadDataState state, CancellationToken ct)
    {
        if (_skillStore == null) return;

        var skillsPath = state.SkillsPath;
        var marker = Path.Combine(state.ThreadDirectory, SkillsWiredMarker);

        // 判定与复制都在锁里：判定要读仓库、比指纹，与另一次运行的复制交错就会看到半截目录。
        var wiringLock = WiringLockFor(state.ThreadDirectory);
        await wiringLock.WaitAsync(ct);
        try
        {
            // 哨兵只由本进程写在线程根下。它若是链接（agent 的命令能在线程根下建链接：Local provider 直接在宿主上跑，
            // Docker 下容器用户与本进程同一 uid 时亦然），读它读的是链接目标、刷新时写它就以本进程身份覆盖链接指向的宿主文件。
            // 只删链接本身，按未接线重来。
            if (SandboxFileSystem.IsLink(marker))
            {
                SandboxFileSystem.RemoveLink(marker);
                _logger.LogWarning("Removed a link planted in place of the skill wiring marker at {Path}", marker);
            }

            List<SkillDefinition> skills;
            try
            {
                skills = await _skillStore.GetAllAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 读不到仓库：已有的副本（如果有）照旧可用，下次再比。
                _logger.LogWarning(ex, "Failed to read the skill store while wiring {Path}", skillsPath);
                return;
            }

            var fingerprint = SkillResourceFingerprint.Of(skills);
            if (IsWired(marker, skillsPath) && ReadFingerprint(marker) == fingerprint) return;

            if (SandboxFileSystem.IsLink(skillsPath))
            {
                SandboxFileSystem.RemoveLink(skillsPath);
                _logger.LogInformation("Replaced legacy skills symlink at {Path} with a per-thread copy", skillsPath);
            }
            else if (Directory.Exists(skillsPath))
            {
                // 旧副本（资源变了、或上次半途失败留下的）整个换掉：只覆盖不删除的话，删掉的技能与文件会一直留着。
                // 删不掉（Windows 上另一次运行正开着其中的文件）就这一次照旧用旧副本，哨兵不动，下次再换。
                try
                {
                    SandboxFileSystem.DeleteTree(skillsPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not replace the skill resource copy at {Path}; keeping it until the next run", skillsPath);
                    return;
                }

                if (File.Exists(marker))
                    _logger.LogInformation("Skill resources changed; refreshing the copy at {Path}", skillsPath);
            }

            if (await CopyAllSkillResourcesAsync(skills, skillsPath, ct))
            {
                await File.WriteAllTextAsync(marker, $"copied at {DateTime.UtcNow:O}\n{FingerprintPrefix}{fingerprint}\n", ct);
            }
        }
        finally
        {
            wiringLock.Release();
        }
    }

    /// <summary>哨兵里记录资源指纹的那一行的前缀。</summary>
    private const string FingerprintPrefix = "fingerprint: ";

    private static bool IsWired(string marker, string skillsPath) => File.Exists(marker) && IsRealDirectory(skillsPath);

    /// <summary>哨兵记下的指纹；没有这一行（这一版之前写的哨兵）时为 null，于是按「已变化」处理、重新复制一次。</summary>
    private static string? ReadFingerprint(string marker)
    {
        foreach (var line in File.ReadLines(marker))
        {
            if (line.StartsWith(FingerprintPrefix, StringComparison.Ordinal))
                return line[FingerprintPrefix.Length..].Trim();
        }

        return null;
    }

    private static SemaphoreSlim WiringLockFor(string threadDirectory)
    {
        var key = OperatingSystem.IsWindows() ? threadDirectory.ToUpperInvariant() : threadDirectory;
        return WiringLocks[(StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % WiringLocks.Length];
    }

    private static bool IsRealDirectory(string path) => Directory.Exists(path) && !SandboxFileSystem.IsLink(path);

    /// <summary>
    /// Copies every loaded skill's resource files into <paramref name="skillsPath"/>.
    /// Returns <c>true</c> on success; the caller writes the completion marker.
    /// </summary>
    private async Task<bool> CopyAllSkillResourcesAsync(IReadOnlyList<SkillDefinition> skills, string skillsPath, CancellationToken ct)
    {
        try
        {
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
            _logger.LogWarning(ex, "Failed to extract skill resources to {Path}", skillsPath);

            try
            {
                if (Directory.Exists(skillsPath)) Directory.Delete(skillsPath, recursive: true);
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                // 清不掉的半截目录不挡下次重试（没有哨兵就会重新复制、逐个覆盖），但要留痕：它会一直占着磁盘。
                _logger.LogWarning(cleanupEx, "Failed to remove the partially copied skills directory {Path}", skillsPath);
            }

            return false;
        }
    }
}
