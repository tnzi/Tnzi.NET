namespace Tnzi.AI.Tools;

/// <summary>
/// 产出物管理工具 - AI Agent 用于标记和呈现文件产物
/// </summary>
[AIToolGroup("artifact")]
public class ArtifactTools
{
    private readonly IAgentArtifactService _artifactService;
    private readonly IAgentExecutionContextAccessor _contextAccessor;

    /// <summary>常见文件扩展名到 MIME 类型映射</summary>
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".md"] = "text/markdown",
        [".txt"] = "text/plain",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".csv"] = "text/csv",
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".zip"] = "application/zip",
        [".py"] = "text/x-python",
        [".cs"] = "text/x-csharp",
        [".ts"] = "text/typescript",
        [".tsx"] = "text/tsx",
        [".jsx"] = "text/jsx",
        [".yaml"] = "application/x-yaml",
        [".yml"] = "application/x-yaml",
        [".sql"] = "application/sql",
        [".sh"] = "application/x-sh",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    public ArtifactTools(IAgentArtifactService artifactService, IAgentExecutionContextAccessor contextAccessor)
    {
        _artifactService = Check.NotNull(artifactService);
        _contextAccessor = Check.NotNull(contextAccessor);
    }

    /// <summary>
    /// 将文件标记为本次运行的产出物，使用户可以预览和下载
    /// </summary>
    [AIFunction("present_files",
        Description = "Present files as artifacts to the user. Call this when you have generated output files that the user should see or download. Provide virtual paths (e.g., /mnt/outputs/report.md).",
        IsConcurrencySafe = true)]
    public async Task<string> PresentFilesAsync(
        [Description("List of virtual file paths to present")] List<string> paths,
        CancellationToken ct = default)
    {
        if (paths is not { Count: > 0 })
            return "No files to present.";

        var request = _contextAccessor.CurrentRequest;
        var threadId = request?.ThreadId ?? Guid.Empty;

        if (threadId == Guid.Empty)
            return "Error: No active thread context. Cannot register artifacts.";

        // RunId 不一定存在（非 EnableRunTracking 模式），使用 Guid.Empty 作为占位
        var runId = Guid.Empty;

        var presented = new List<string>();
        var errors = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                var fileName = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    errors.Add($"Invalid path: {path}");
                    continue;
                }

                var contentType = InferMimeType(fileName);
                var result = await _artifactService.CreateAsync(
                    runId, threadId, path, fileName, contentType, null, ct);

                if (result.Succeeded)
                    presented.Add(fileName);
                else
                    errors.Add($"{fileName}: {result.Message}");
            }
            catch (Exception ex)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        var sb = new StringBuilder();
        if (presented.Count > 0)
            sb.AppendLine($"Successfully presented {presented.Count} file(s): {string.Join(", ", presented)}");
        if (errors.Count > 0)
            sb.AppendLine($"Errors: {string.Join("; ", errors)}");

        return sb.ToString();
    }

    /// <summary>
    /// 根据文件名推断 MIME 类型
    /// </summary>
    private static string? InferMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return null;
        return MimeTypes.GetValueOrDefault(ext);
    }
}
