namespace Tnzi.AI.Infrastructure.Documents;

/// <summary>
/// 基于 CLI 工具的文档转换器（Pandoc / pdftotext / LibreOffice）
/// </summary>
public class CliDocumentConverter : IDocumentConverter
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
    };

    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
    };

    private readonly ILogger<CliDocumentConverter> _logger;

    public CliDocumentConverter(ILogger<CliDocumentConverter> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public bool CanConvert(string extension)
    {
        return SupportedExtensions.Contains(extension);
    }

    /// <inheritdoc />
    public async Task<string?> ConvertToMarkdownAsync(string filePath, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File not found: {FilePath}", filePath);
            return null;
        }

        var extension = Path.GetExtension(filePath);
        if (!CanConvert(extension))
        {
            _logger.LogWarning("Unsupported file extension: {Extension}", extension);
            return null;
        }

        try
        {
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return await ConvertPdfAsync(filePath, ct);
            }

            if (OfficeExtensions.Contains(extension))
            {
                return await ConvertOfficeAsync(filePath, ct);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CLI conversion failed for: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// PDF 转换：优先 Pandoc，回退 pdftotext
    /// </summary>
    private async Task<string?> ConvertPdfAsync(string filePath, CancellationToken ct)
    {
        // 尝试 Pandoc
        var result = await RunProcessAsync("pandoc", $"\"{filePath}\" -t markdown", ct);
        if (result != null)
        {
            return result;
        }

        // 回退到 pdftotext
        result = await RunProcessAsync("pdftotext", $"-layout \"{filePath}\" -", ct);
        return result;
    }

    /// <summary>
    /// Office 转换：LibreOffice → HTML → Pandoc → Markdown
    /// </summary>
    private async Task<string?> ConvertOfficeAsync(string filePath, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tnzi_doc_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // LibreOffice 转 HTML
            var libreResult = await RunProcessAsync(
                "libreoffice",
                $"--headless --convert-to html --outdir \"{tempDir}\" \"{filePath}\"",
                ct);

            if (libreResult == null)
            {
                _logger.LogDebug("LibreOffice conversion failed for: {FilePath}", filePath);
                return null;
            }

            var htmlFile = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(filePath) + ".html");
            if (!File.Exists(htmlFile))
            {
                _logger.LogDebug("LibreOffice output HTML not found: {HtmlFile}", htmlFile);
                return null;
            }

            // Pandoc HTML → Markdown
            var markdown = await RunProcessAsync("pandoc", $"\"{htmlFile}\" -f html -t markdown", ct);
            return markdown;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }
    }

    /// <summary>
    /// 执行外部进程并捕获标准输出
    /// </summary>
    private async Task<string?> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogDebug("Failed to start process: {FileName}", fileName);
                return null;
            }

            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                if (process.ExitCode != 0)
                {
                    _logger.LogDebug("{FileName} exited with code {ExitCode}", fileName, process.ExitCode);
                    return null;
                }

                return string.IsNullOrWhiteSpace(output) ? null : output;
            }
            catch (OperationCanceledException)
            {
                // 取消时终止外部进程，避免进程泄漏
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Process execution failed: {FileName}", fileName);
            return null;
        }
    }
}
