namespace Tnzi.AI.Sandbox.Services;

/// <summary>
/// Application-startup helper that extracts every <c>ISkillStore</c> skill's
/// attached resource files into a single read-only directory under
/// <c>{DataRoot}/_skills/</c>. Subsequent thread setup symlinks
/// <c>{ThreadDir}/skills</c> to this shared root instead of per-thread
/// copying.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the previous behaviour (every thread re-extracts all built-in
/// skills into its private <c>{ThreadDir}/skills/</c>). For an app running
/// 100 threads with the 19 built-in skills, the duplicated bytes added up
/// to GB-scale waste — closed by this single-extraction-plus-symlink design.
/// </para>
/// <para>
/// Extracted files are marked read-only (`FileAttributes.ReadOnly` /
/// Unix 0444) so that an agent that follows the symlink and tries to
/// write into <c>/mnt/skills/&lt;slug&gt;/</c> hits a permission error
/// instead of silently mutating shared state across all threads.
/// </para>
/// <para>
/// The whole routine is best-effort: any IO failure is logged at Warning
/// and silently swallowed. <see cref="Middleware.ThreadDataMiddleware"/>
/// still has a per-thread copy fallback path for systems that do not
/// support symbolic links (notably non-developer-mode Windows hosts).
/// </para>
/// </remarks>
public static class SharedSkillExtractor
{
    public const string SharedDirectoryName = "_skills";
    public const string ExtractedMarker = ".extracted";

    /// <summary>
    /// Extracts all skill resources into <c>{dataRoot}/_skills/</c>. Idempotent
    /// via the <c>.extracted</c> marker file — running twice is a no-op.
    /// </summary>
    public static async Task ExtractAsync(
        ISkillStore skillStore,
        string dataRoot,
        ILogger logger,
        CancellationToken ct = default)
    {
        Check.NotNull(skillStore);
        Check.NotNullOrWhiteSpace(dataRoot);
        Check.NotNull(logger);

        var sharedRoot = Path.Combine(dataRoot, SharedDirectoryName);
        var markerPath = Path.Combine(sharedRoot, ExtractedMarker);

        if (File.Exists(markerPath))
        {
            logger.LogDebug("Shared skill resources already extracted at {Path}", sharedRoot);
            return;
        }

        try
        {
            Directory.CreateDirectory(sharedRoot);

            var skills = await skillStore.GetAllAsync(ct);
            var fileCount = 0;
            var skillCount = 0;

            foreach (var skill in skills)
            {
                if (skill.Resources.Count == 0) continue;

                var skillDir = Path.Combine(sharedRoot, skill.Slug);
                Directory.CreateDirectory(skillDir);

                foreach (var (relativePath, content) in skill.Resources)
                {
                    var filePath = Path.Combine(skillDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(filePath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    await File.WriteAllTextAsync(filePath, content, ct);

                    // Mark every extracted file read-only so that agents following
                    // the per-thread symlink cannot poison the shared root.
                    TrySetReadOnly(filePath, logger);
                    fileCount++;
                }
                skillCount++;
            }

            await File.WriteAllTextAsync(markerPath, $"{skillCount} skills / {fileCount} files extracted at {DateTime.UtcNow:O}", ct);
            logger.LogInformation(
                "Extracted {SkillCount} skills ({FileCount} resource files) to shared root {Path}",
                skillCount, fileCount, sharedRoot);
        }
        catch (Exception ex)
        {
            // Best-effort: leave the marker absent so ThreadDataMiddleware falls
            // back to per-thread extraction. Better degraded than crashing.
            logger.LogWarning(ex, "Failed to populate shared skill root at {Path}; threads will fall back to per-thread extraction", sharedRoot);
        }
    }

    private static void TrySetReadOnly(string filePath, ILogger logger)
    {
        try
        {
            File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.ReadOnly);
        }
        catch (Exception ex)
        {
            // Read-only is defence-in-depth; missing it is not fatal.
            logger.LogDebug(ex, "Could not set read-only on {Path}", filePath);
        }
    }

    /// <summary>Resolves the shared root path for a given data root.</summary>
    public static string GetSharedRoot(string dataRoot)
        => Path.Combine(dataRoot, SharedDirectoryName);
}
