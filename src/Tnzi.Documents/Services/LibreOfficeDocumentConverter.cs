using System.ComponentModel;

namespace Tnzi.Documents.Services;

/// <summary>
/// 默认的 Office 转 PDF 实现：调用本机 LibreOffice 的 headless 转换。
/// </summary>
/// <remarks>
/// <para><b>命令行形态是实测出来的，改动前先读完这段。</b></para>
/// <code>
/// soffice --headless --norestore -env:UserInstallation=file:///&lt;profile&gt; --convert-to pdf --outdir &lt;out&gt; &lt;input&gt;
/// </code>
/// <list type="bullet">
/// <item><b>绝不能带 <c>--nodefault</c></b>：它与 <c>--convert-to</c> 同时出现时进程会**永久挂起**
/// （实测 7 分钟无任何进展、CPU 归零、不退出）。<c>--nolockcheck</c> / <c>--nofirststartwizard</c>
/// 也没有必要，一并不要。</item>
/// <item>重定向输出必须**先起读再等退出**，否则管道写满会双向死锁。</item>
/// </list>
/// <para><b>并发策略 = 全局串行（一个信号量 + 一个复用的 profile 目录），请不要"优化"成并发。</b>
/// 实测：串行 6/6 全部成功、均摊 2.0s；4 路并发各自新建 profile 只成功 1/4，失败者退出码
/// <c>0xC000041D</c>（STATUS_FATAL_USER_CALLBACK_EXCEPTION）；4 路共用一个 profile 成功 2/4，
/// 失败者退出码 1 —— 冷 profile 的并发引导会崩。唯一能安全并发的形态是"预热过的 profile 池"
/// （实测 4/4 通过），但转换是低频操作（只在管理员上传模板时发生），不值得为此增加复杂度。</para>
/// <para>计时基准：冷 profile 首次约 4.9s，之后暖启动约 1.4s，故默认超时 120s 足够宽裕。
/// 超时会杀掉整棵进程树，否则会留下孤儿 <c>soffice.bin</c>。</para>
/// </remarks>
public sealed class LibreOfficeDocumentConverter : IDocumentConverter
{
    /// <summary>
    /// 进程级串行闸门。刻意是 static：即使 DI 生命周期变化或有人手工 new 出第二个实例，
    /// 也仍然只有一路转换在跑（并发引导会崩，见类注释）。
    /// </summary>
    private static readonly SemaphoreSlim ConversionGate = new(1, 1);

    /// <summary>转换用的临时工作目录根（逐次创建、用完即删）。</summary>
    private const string WorkRootName = "tnzi-doc-convert";

    /// <summary>默认的 LibreOffice profile 目录名（长期复用，不逐次删）。</summary>
    private const string DefaultProfileName = "tnzi-libreoffice-profile";

    /// <summary>转换出来的中间文件基名，与上传文件名无关（见 <see cref="ConvertToPdfAsync"/>）。</summary>
    private const string WorkFileBaseName = "source";

    private const int CapturedOutputLimit = 2000;

    private readonly IOptions<DocumentsOptions> _options;
    private readonly ILogger<LibreOfficeDocumentConverter> _logger;

    /// <summary>初始化一个 <see cref="LibreOfficeDocumentConverter"/> 实例。</summary>
    /// <param name="options">文档原语配置。</param>
    /// <param name="logger">日志。</param>
    public LibreOfficeDocumentConverter(IOptions<DocumentsOptions> options, ILogger<LibreOfficeDocumentConverter> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 即 <c>soffice</c> 找不找得到。<see cref="LibreOfficeLocator.Resolve"/> 按配置值缓存探测结果，
    /// 所以列表页逐行询问也只有首次真正碰文件系统。
    /// </remarks>
    public bool IsAvailable => LibreOfficeLocator.Resolve(_options.Value.LibreOfficePath) != null;

    /// <inheritdoc />
    public bool CanConvert(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && DocumentFormats.ConvertibleExtensions.Contains(extension);
    }

    /// <inheritdoc />
    public async Task<byte[]> ConvertToPdfAsync(byte[] source, string sourceFileName, CancellationToken ct = default)
    {
        Check.NotNull(source);
        Check.NotNullOrWhiteSpace(sourceFileName);

        if (source.Length == 0)
            throw new DocumentConversionException($"Source document '{sourceFileName}' is empty.");

        if (!CanConvert(sourceFileName))
        {
            throw new DocumentConversionException(
                $"'{Path.GetExtension(sourceFileName)}' is not a convertible document format. " +
                $"Supported: {string.Join(", ", DocumentFormats.ConvertibleExtensions.Order(StringComparer.Ordinal))}.");
        }

        var options = _options.Value;
        var executable = LibreOfficeLocator.Resolve(options.LibreOfficePath)
            ?? throw new DocumentConversionException(LibreOfficeLocator.NotFoundMessage(options.LibreOfficePath));

        // 只取扩展名、且已过白名单：上传文件名不参与命令行，杜绝借文件名做参数注入。
        var extension = Path.GetExtension(sourceFileName);
        var workDirectory = Path.Combine(Path.GetTempPath(), WorkRootName, Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(workDirectory, "out");

        try
        {
            Directory.CreateDirectory(outputDirectory);

            var inputPath = Path.Combine(workDirectory, WorkFileBaseName + extension);
            await File.WriteAllBytesAsync(inputPath, source, ct);

            var profileDirectory = EnsureProfileDirectory(options);
            var timeout = TimeSpan.FromSeconds(options.ConversionTimeoutSeconds);

            await ConversionGate.WaitAsync(ct);
            try
            {
                await RunAsync(executable, profileDirectory, inputPath, outputDirectory, timeout, ct);
            }
            finally
            {
                ConversionGate.Release();
            }

            var pdfPath = Path.Combine(outputDirectory, WorkFileBaseName + DocumentFormats.PdfExtension);
            if (!File.Exists(pdfPath))
            {
                throw new DocumentConversionException(
                    $"LibreOffice reported success but produced no PDF for '{sourceFileName}'. " +
                    "The source document may be corrupt or password protected.");
            }

            var pdf = await File.ReadAllBytesAsync(pdfPath, ct);
            _logger.LogDebug("Converted '{FileName}' to PDF ({Bytes} bytes).", sourceFileName, pdf.Length);
            return pdf;
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    private async Task RunAsync(
        string executable,
        string profileDirectory,
        string inputPath,
        string outputDirectory,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = outputDirectory
        };

        // 逐个塞 ArgumentList（而不是拼一条 Arguments 字符串）：路径里的空格与引号由运行时负责转义。
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--norestore");
        startInfo.ArgumentList.Add($"-env:UserInstallation={new Uri(profileDirectory).AbsoluteUri}");
        startInfo.ArgumentList.Add("--convert-to");
        startInfo.ArgumentList.Add("pdf");
        startInfo.ArgumentList.Add("--outdir");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add(inputPath);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
                throw new DocumentConversionException($"Failed to start LibreOffice at '{executable}'.");
        }
        catch (Win32Exception ex)
        {
            throw new DocumentConversionException($"Failed to start LibreOffice at '{executable}': {ex.Message}", innerException: ex);
        }

        // 先起读、再等退出：反过来会在子进程写满管道时双向死锁。
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            // 不杀就会留下孤儿 soffice.bin，并且它还占着 profile 目录
            KillTree(process);

            ct.ThrowIfCancellationRequested();
            throw new DocumentConversionException(
                $"LibreOffice conversion timed out after {timeout.TotalSeconds:0} seconds. " +
                "Raise 'Documents:ConversionTimeoutSeconds' if large documents are expected.",
                isRetryable: true);
        }

        var output = await standardOutput;
        var error = await standardError;

        if (process.ExitCode != 0)
        {
            _logger.LogWarning(
                "LibreOffice exited with code {ExitCode}. stdout: {StandardOutput} stderr: {StandardError}",
                process.ExitCode, Truncate(output), Truncate(error));

            throw new DocumentConversionException(
                $"LibreOffice exited with code {process.ExitCode}. {Truncate(error)}".TrimEnd());
        }
    }

    /// <summary>
    /// 确保 profile 目录存在。**长期复用、不逐次删** —— 冷 profile 引导要多花几秒，且并发新建会崩。
    /// </summary>
    private static string EnsureProfileDirectory(DocumentsOptions options)
    {
        var directory = string.IsNullOrWhiteSpace(options.ProfileDirectory)
            ? Path.Combine(Path.GetTempPath(), DefaultProfileName)
            : options.ProfileDirectory;

        Directory.CreateDirectory(directory);
        return directory;
    }

    private void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception or AggregateException)
        {
            // 进程已自行退出，或平台不支持杀进程树：记录即可，不能盖过原始的超时/取消
            _logger.LogDebug(ex, "Failed to kill the LibreOffice process tree after a timeout or cancellation.");
        }
    }

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响转换结果，但要留痕（临时目录堆积是可观测的运维问题）
            _logger.LogWarning(ex, "Failed to clean up the conversion work directory '{Directory}'.", directory);
        }
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.Length <= CapturedOutputLimit ? trimmed : trimmed[..CapturedOutputLimit] + "...";
    }
}
