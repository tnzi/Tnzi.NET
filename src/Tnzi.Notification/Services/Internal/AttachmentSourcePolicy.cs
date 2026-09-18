namespace Tnzi.Notification.Services.Internal;

/// <summary>
/// 附件来源纪律：一个 <c>FilePath</c>（本地路径或 URL）允不允许被读成附件。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>补的是哪个洞。</b><c>FilePath</c> 从管理端请求体原样落库，发信时内置发送器按它读<b>任意本地文件</b>
/// 或取<b>任意 URL</b> 当附件，而收件人地址在同一个请求体里。持 <c>notification.message.create</c> 的人
/// 一次请求就能把服务端的生产配置寄到自己邮箱，或对云元数据 / 内网端点做一次带回显的 SSRF ——
/// 整条链路的日志只记一次正常投递。核心早有 <see cref="EgressGuard"/>，这里一处都没用。
/// </para>
/// <para>
/// ★ <b>同一条规则，两处调用。</b>入口（<c>NotificationService</c> 创建那一刻，400 就地拒绝）与
/// 发送器（<c>MailKitEmailSender</c> 取件之前，落库后 DNS 可能已变、旧行与消费方直接调用也要挡）
/// 都调这一个方法。规则只写一份 —— 本模块的规则漂开过不止一次，每次都是同一条抄了两遍。
/// </para>
/// <para>
/// ★ <b>方向 fail-closed。</b>没有任何允许的根目录 = 拒绝一切本地路径；解析不出的路径拒绝；
/// 不是 http/https 的 scheme 拒绝；DNS 解析失败拒绝（<see cref="EgressGuard"/> 的既定口径）。
/// </para>
/// </remarks>
internal static class AttachmentSourcePolicy
{
    /// <summary>
    /// 判定 <paramref name="filePath"/> 是否允许作为附件来源。可以时返回 <see langword="null"/>，
    /// 否则返回一句说得清原因、可直接回给调用方的英文说明。
    /// </summary>
    /// <param name="filePath">附件的本地路径或 URL；空白 = 没有路径可取，不归本策略管。</param>
    /// <param name="options">附件来源配置。</param>
    /// <param name="cancellationToken">取消令牌（URL 分支要解析 DNS）。</param>
    public static async Task<string?> DescribeViolationAsync(string? filePath, AttachmentOptions options, CancellationToken cancellationToken)
    {
        Check.NotNull(options);

        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        if (IsRemoteUrl(filePath, out var uri))
        {
            if (!options.AllowRemoteUrls)
                return $"Remote attachment URLs are disabled (Notification:Attachments:AllowRemoteUrls is false): '{filePath}'.";

            // EgressGuard 同时挡住非 http/https 的 scheme 与私网 / 链路本地 / loopback 地址，
            // 并解析全部 DNS 结果。
            var blocked = await EgressGuard.CheckUriAsync(uri, cancellationToken);
            return blocked == null ? null : $"Attachment URL '{filePath}' is not allowed: {blocked}";
        }

        // 'file:' URI 不当本地路径收：File.Exists 对它恒假，而按字符串归一化会得到一个看着合法的路径。
        if (filePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return $"Attachment path '{filePath}' is not allowed: file: URIs are not accepted, pass a plain local path.";

        return DescribeLocalPathViolation(filePath, options.AllowedLocalRoots ?? []);
    }

    /// <summary>
    /// <paramref name="filePath"/> 是不是一个远程 URL（绝对 URI 且不是 file:）。
    /// 与发送器判定"走下载还是读文件"的是同一个判据，故放在这里共用。
    /// </summary>
    public static bool IsRemoteUrl(string filePath, out Uri uri)
    {
        if (Uri.TryCreate(filePath, UriKind.Absolute, out var parsed) && !parsed.IsFile)
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static string? DescribeLocalPathViolation(string filePath, IReadOnlyList<string> allowedRoots)
    {
        if (allowedRoots.Count == 0)
            return $"Local attachment paths are not allowed: no Notification:Attachments:AllowedLocalRoots is configured ('{filePath}').";

        // 相对路径取决于进程的工作目录 —— 同一个字符串在两个部署上指向两个地方，不接受。
        if (!Path.IsPathFullyQualified(filePath))
            return $"Attachment path '{filePath}' is not allowed: it must be an absolute path.";

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"Attachment path '{filePath}' is not a valid local path.";
        }

        if (!IsUnderAnyRoot(fullPath, allowedRoots))
            return $"Attachment path '{filePath}' is outside the allowed local roots (Notification:Attachments:AllowedLocalRoots).";

        // 链接逃逸：路径的每一级都在根之下，而其中某一级（目录或叶子）是指向根之外的链接。只对存在的部分解析得到。
        return DescribeLinkViolation(filePath, fullPath, allowedRoots);
    }

    private static bool IsUnderAnyRoot(string fullPath, IReadOnlyList<string> allowedRoots)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            string rootPrefix;
            try
            {
                // 尾部补上分隔符，否则 /roots/x 会放行 /roots/x-evil/…
                // 文件系统根（C:\ / /）本身就以分隔符结尾，再补一个谁都匹配不上（TrimEndingDirectorySeparator 不动根路径）。
                var fullRoot = Path.GetFullPath(root);
                rootPrefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (fullPath.StartsWith(rootPrefix, comparison))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 路径上任何一级是链接时（目录或叶子文件），把整条路径解析到最终目标，目标也必须在某个根之下。
    /// </summary>
    /// <remarks>
    /// ★ <b>不能只看叶子。</b><c>Path.GetFullPath</c> 不解析链接，而 <c>FileInfo.LinkTarget</c> 对一个
    /// 普通文件恒为 <see langword="null"/> —— 哪怕它的父目录是 <c>root/etc -> /etc</c>。前缀检查过、叶子不是链接，
    /// 只看叶子的检查一处都拦不住。部署树里目录链接很常见（<c>current -> releases/N</c>、<c>logs -> /var/log/…</c>），
    /// 所以逐级解析，并把指向另一个允许根之下的链接照常放行。
    /// </remarks>
    private static string? DescribeLinkViolation(string filePath, string fullPath, IReadOnlyList<string> allowedRoots)
    {
        string target;
        try
        {
            target = ResolveLinks(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 解析不了就拒绝：链接目标看不见时放行，等于把这道检查交给运气。
            return $"Attachment path '{filePath}' is not allowed: its link target could not be resolved ({ex.Message}).";
        }

        return IsUnderAnyRoot(target, allowedRoots)
            ? null
            : $"Attachment path '{filePath}' is outside the allowed local roots: it links to '{target}'.";
    }

    /// <summary>
    /// 从根开始逐级拼回 <paramref name="fullPath"/>，每一级存在且是链接就换成它的最终目标再往下拼。
    /// 没有任何一级是链接时返回的就是 <paramref name="fullPath"/> 本身。
    /// </summary>
    private static string ResolveLinks(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var segments = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        for (var i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);

            // 中间一级只可能是目录；叶子按文件看（叶子是目录时它本来就不是附件，后面 File.Exists 会拒）。
            FileSystemInfo info = i == segments.Length - 1 ? new FileInfo(current) : new DirectoryInfo(current);
            if (!info.Exists || info.LinkTarget == null)
                continue;

            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            if (resolved != null)
                current = resolved.FullName;
        }

        return Path.GetFullPath(current);
    }
}
