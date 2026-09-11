namespace Tnzi.Storage.Controllers;

/// <summary>
/// 存储控制器基类
/// 提供文件上传、下载、版本、分享、分块上传等 API 的抽象基类
/// </summary>
/// <remarks>
/// 几个读端点带 <c>[AllowAnonymous]</c>:
/// 一是让 <c>FileRecord.IsPublic</c> 的文件(头像 / 站点素材)能被未登录访客取到,
/// 二是让**对外分享链接**能被真正的外部收件人打开 —— 收件人没有账号才叫对外分享。
/// 类级 <c>[ApiAuthorize]</c> 会把它们一并挡掉。
///
/// **放行的是路由,不是数据**:真正的判定在 <see cref="IFileAccessAuthorizer"/>,由
/// <see cref="IFileStorageService"/> 在每次按 id 取记录时执行(公开 / 部署级开关 /
/// 归属 / <c>storage.file.view</c> 四者之一)。不通过一律 404,不泄露该 id 上是否有文件。
///
/// 判定刻意不放在控制器:本类是 <c>[DefaultController]</c>,消费方可在同路由注册自己的
/// 控制器把它整个替换掉,那样挂在这里的任何特性都会随之失效。服务层是唯一必经之处。
///
/// ★ <b>分享 / 版本 / 分片上传三组端点留在这里,而它们的实现在可选包
/// <c>Tnzi.Storage.Workspace</c></b>。三个服务因此是<b>可空可选注入</b>,没加载时那些端点
/// 返回 501 并指名要加载的包 —— <b>URL 一个字不变</b>。
///
/// 为什么不把它们搬进子模块自己的控制器:<c>[DefaultController]</c> 是 <c>Inherited = false</c>
/// 而 <c>[Route]</c> 是 <c>Inherited = true</c>,子模块在 <c>files</c> 这个<b>父模块仍然占着的</b>
/// 路由模板上另起一个默认控制器,会让「消费方派生 <c>DefaultStorageController</c> 覆写一个端点」
/// 这条框架文档给出的做法产生它没有要过的副作用。另起路由前缀则会改掉公开 URL,
/// 那是对所有既有调用方的破坏性变更,不该由一次内部拆分来付。
/// </remarks>
[DefaultController]
[Route("files")]
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultStorageController : ApiControllerBase
{
    protected readonly IFileStorageService FileStorageService;

    /// <summary>分享链接服务；<c>null</c> = 未加载 <c>Tnzi.Storage.Workspace</c>。</summary>
    protected readonly IFileShareService? FileShareService;

    /// <summary>文件版本服务；<c>null</c> = 未加载 <c>Tnzi.Storage.Workspace</c>。</summary>
    protected readonly IFileVersionService? FileVersionService;

    /// <summary>分片上传服务；<c>null</c> = 未加载 <c>Tnzi.Storage.Workspace</c>。</summary>
    protected readonly IFileChunkUploadService? FileChunkUploadService;

    /// <summary>
    /// 构造函数
    /// </summary>
    public DefaultStorageController(
        IFileStorageService fileStorageService,
        IFileShareService? fileShareService = null,
        IFileVersionService? fileVersionService = null,
        IFileChunkUploadService? fileChunkUploadService = null)
    {
        FileStorageService = Check.NotNull(fileStorageService);
        FileShareService = fileShareService;
        FileVersionService = fileVersionService;
        FileChunkUploadService = fileChunkUploadService;
    }

    /// <summary>
    /// 工作区包缺席时的统一回答：<b>501 + 指名要加载什么</b>。
    /// </summary>
    /// <remarks>
    /// 用 501 而不是 404：路由确实存在，只是这台宿主没装这项能力，404 会让调用方
    /// 以为自己拼错了 URL，从而去查一个不存在的问题。
    /// 也不用 503：那是「暂时不可用」，监控与客户端的重试逻辑会照着它一直重试一件
    /// 永远不会变好的事。501 恰好就是「这台服务器不提供这项功能」，
    /// 也是框架内既有的同类回答（Finance 的 ICheckDocumentRenderer / IReceiptExtractor 缺席时同样是 501）。
    /// 消息里点名包名，是因为「少加载一个可选包」在日志里唯一能自证的方式就是它自己说出来。
    /// </remarks>
    private const string WorkspaceMissing =
        "This capability requires the Tnzi.Storage.Workspace module, which this host has not loaded.";

    /// <summary>
    /// 根据 ID 获取文件
    /// </summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public virtual async Task<ApiResult<FileRecordDto>> GetById(Guid id)
    {
        var result = await FileStorageService.GetRecordAsync(id);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 删除文件
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await FileStorageService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 上传文件
    /// </summary>
    /// <param name="file">文件内容</param>
    /// <param name="isPublic">
    /// 标记为公开可读:该文件之后可被任何人(含未登录访客)按 id 取到,用于头像、站点素材这类
    /// 以匿名 <c>&lt;img src&gt;</c> 消费的资源。默认 false。
    ///
    /// 注意这是**上传者自报**的意图,不是唯一的公开途径:文件 id 写进标了
    /// <c>[FileField(Public = true)]</c> 的实体字段时,框架会自行补上公开标记,
    /// 所以头像不会因为前端漏传这个参数而失效。
    /// </param>
    [HttpPost("upload")]
    public virtual async Task<ApiResult<FileRecordDto>> Upload(IFormFile file, [FromForm] bool isPublic = false)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest<FileRecordDto>("File is required");
        }

        using var stream = file.OpenReadStream();
        var result = await FileStorageService.SaveAsync(file.FileName, stream, isTemporary: true, isPublic: isPublic);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 批量上传
    /// </summary>
    [HttpPost("upload/batch")]
    public virtual async Task<ApiResult<IEnumerable<FileRecordDto>>> UploadMany(IEnumerable<IFormFile> files, [FromForm] bool isPublic = false)
    {
        if (files == null || !files.Any())
        {
            return BadRequest<IEnumerable<FileRecordDto>>("Files are required");
        }

        var fileList = new List<(string fileName, Stream stream)>();
        var streams = new List<Stream>();

        try
        {
            foreach (var file in files)
            {
                var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                streams.Add(memoryStream);
                fileList.Add((file.FileName, memoryStream));
            }

            var result = await FileStorageService.SaveManyAsync(fileList, isPublic);
            return result.Map(items => items.Select(r => r.MapTo<FileRecordDto>())).ToApiResult();
        }
        finally
        {
            foreach (var stream in streams)
            {
                stream.Dispose();
            }
        }
    }

    /// <summary>
    /// 根据 ID 下载文件
    /// </summary>
    [HttpGet("{id:guid}/download")]
    [AllowAnonymous]
    public virtual async Task<IActionResult> Download(Guid id)
    {
        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var record = recordResult.Data;

        // 处理 Range 请求
        var rangeHeader = Request.Headers.Range.FirstOrDefault();
        if (!string.IsNullOrEmpty(rangeHeader))
        {
            var (rangeStart, rangeEnd) = ParseRangeHeader(rangeHeader, record!.Size);
            if (rangeStart.HasValue)
            {
                var rangeResult = await FileStorageService.GetRangeAsync(id, rangeStart, rangeEnd);
                if (!rangeResult.Succeeded)
                {
                    return new NotFoundResult();
                }
                var (stream, start, end, totalLength) = rangeResult.Data!;

                Response.Headers.ContentRange = $"bytes {start}-{end}/{totalLength}";
                Response.Headers.AcceptRanges = "bytes";
                Response.StatusCode = 206;
                Response.ContentLength = end - start + 1;

                return File(stream, record.ContentType ?? "application/octet-stream",
                    record.OriginalName ?? record.FileName,
                    enableRangeProcessing: true);
            }
        }

        var fullStreamResult = await FileStorageService.GetAsync(id);
        if (!fullStreamResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var fullStream = fullStreamResult.Data!;
        Response.Headers.AcceptRanges = "bytes";
        Response.ContentLength = record!.Size;
        return File(fullStream, record!.ContentType ?? "application/octet-stream",
            record!.OriginalName ?? record!.FileName);
    }

    /// <summary>
    /// 根据 ID 获取缩略图
    /// </summary>
    [HttpGet("{id:guid}/thumbnail")]
    [AllowAnonymous]
    public virtual async Task<IActionResult> GetThumbnail(Guid id)
    {
        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var record = recordResult.Data!;

        var thumbnailResult = await FileStorageService.GetThumbnailAsync(id);
        if (!thumbnailResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var thumbnailStream = thumbnailResult.Data!;

        return File(thumbnailStream, "image/jpeg", $"thumb_{record.FileName}");
    }

    /// <summary>
    /// 根据 ID 预览文件（内联交给浏览器渲染）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>只有可展示的类型才内联</b>（<see cref="FileTypeHelper.IsInlineRenderable"/>：位图 / 视频 / 音频 /
    /// PDF / 纯文本），其余一律带文件名 ⇒ <c>Content-Disposition: attachment</c>，与 <see cref="Download"/> 同形。
    /// <c>FileRecord.ContentType</c> 是按<b>上传者给的文件名</b>算出来的（<c>.html → text/html</c>），
    /// 而本端点匿名可达、还被缓存一年：不加这道闸，任何已登录用户传一个 <c>payload.html</c>
    /// 并标 <c>isPublic</c>，再把预览链接发出去，脚本就跑在 API 的源上。
    /// </para>
    /// <para>
    /// 附件分支<b>保留声明的类型</b>而不是改成 <c>application/octet-stream</c>：<c>&lt;img src&gt;</c> 里的 .svg
    /// 仍要渲染得出来（子资源加载不理会 Content-Disposition，而 <c>&lt;img&gt;</c> 是脚本不执行的上下文），
    /// 换成 octet-stream 会让 nosniff 下的图片加载被浏览器整个拒掉。<c>Content-Security-Policy: sandbox</c>
    /// 是纵深：万一某个上下文仍把它当文档打开，脚本禁用、源不透明。
    /// </para>
    /// <para>
    /// <c>nosniff</c> 对两个分支都加：声明的类型就是最终类型，纯文本才不会被嗅探成 HTML。
    /// 这不是授权判定（那仍只在 <see cref="IFileAccessAuthorizer"/>），是响应形态；
    /// 消费方整体替换本控制器时请沿用 <see cref="FileTypeHelper.IsInlineRenderable"/>。
    /// </para>
    /// </remarks>
    [HttpGet("{id:guid}/preview")]
    [AllowAnonymous]
    public virtual async Task<IActionResult> Preview(Guid id)
    {
        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var record = recordResult.Data!;

        var streamResult = await FileStorageService.GetAsync(id);
        if (!streamResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var stream = streamResult.Data!;

        var contentType = record.ContentType ?? "application/octet-stream";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public, max-age=31536000";

        if (!FileTypeHelper.IsInlineRenderable(contentType))
        {
            Response.Headers.ContentSecurityPolicy = "sandbox";
            return File(stream, contentType, record.OriginalName ?? record.FileName);
        }

        return File(stream, contentType);
    }

    /// <summary>
    /// 获取文件访问 URL
    /// </summary>
    [HttpGet("{id:guid}/url")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<string>> GetUrl(Guid id, [FromQuery] int? expiresIn = null)
    {
        var result = await FileStorageService.GetUrlAsync(id, expiresIn);
        return result.ToApiResult();
    }

    /// <summary>
    /// Generate a presigned URL for temporary public access
    /// </summary>
    [HttpGet("{id:guid}/presigned-url")]
    [SensitiveEndpoint(
        "storage.presigned-url",
        "Issues a URL that the object store serves directly. It bypasses application-layer authorization "
        + "entirely, and for cloud providers its lifetime is governed by the storage backend rather than by "
        + "Storage:SignedUrlTtlSeconds - so the framework can neither shorten nor revoke it once handed out.")]
    public virtual async Task<ApiResult<string>> GetPresignedUrl(Guid id, [FromQuery] int expiresInSeconds = 3600)
    {
        var result = await FileStorageService.GetPresignedUrlAsync(id, expiresInSeconds, "GET");
        return result.ToApiResult();
    }

    /// <summary>
    /// Mint a short-lived access token so a browser can fetch this private file
    /// without an Authorization header.
    /// </summary>
    /// <remarks>
    /// Append the token to any read URL of the same file as the `sig` query
    /// parameter: <c>/api/files/{id}/preview?sig={token}</c>. It exists because
    /// `&lt;img&gt;`, `&lt;a download&gt;` and `&lt;video&gt;` cannot send a bearer
    /// token, which otherwise makes every private file unrenderable - even for
    /// the person who uploaded it.
    ///
    /// Minting runs the full read check, so a caller who cannot read the file
    /// cannot mint a token for it (404, same as every other read path).
    /// </remarks>
    [HttpGet("{id:guid}/access-token")]
    [SensitiveEndpoint(
        "storage.access-token",
        "Issues a credential that can leave the controlled environment (it travels in a URL query string). "
        + "Blast radius is deliberately small - one file, read-only, capped by Storage:SignedUrlTtlSeconds, "
        + "and it cannot mint further tokens - but a leaked link is readable by anyone until it expires.")]
    public virtual async Task<ApiResult<FileAccessTokenDto>> GetAccessToken(Guid id, [FromQuery] int? expiresInSeconds = null)
    {
        var result = await FileStorageService.CreateAccessTokenAsync(id, expiresInSeconds);
        return result.ToApiResult();
    }

    /// <summary>
    /// Mint access tokens for several files at once. Files the caller cannot
    /// read are omitted from the response instead of failing the batch.
    /// </summary>
    [HttpPost("access-tokens")]
    [SensitiveEndpoint(
        "storage.access-token",
        "Batch form of the single-file access token: same credential, many files in one round trip. "
        + "Shares the capability name so that suppressing it closes both.")]
    public virtual async Task<ApiResult<IReadOnlyList<FileAccessTokenDto>>> GetAccessTokens([FromBody] FileAccessTokenRequest request)
    {
        if (request?.FileIds == null || request.FileIds.Count == 0)
        {
            return BadRequest<IReadOnlyList<FileAccessTokenDto>>("FileIds are required");
        }

        var result = await FileStorageService.CreateAccessTokensAsync(request.FileIds, request.ExpiresInSeconds);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重命名文件
    /// </summary>
    [HttpPut("{id:guid}/rename")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileRecordDto>> Rename(Guid id, [FromBody] RenameFileRequest request)
    {
        var result = await FileStorageService.RenameAsync(id, request.NewFileName);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 复制文件
    /// </summary>
    [HttpPost("{id:guid}/copy")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileRecordDto>> CopyFile(Guid id, [FromBody] CopyFileRequest? request = null)
    {
        var newFileName = request?.NewFileName;
        var result = await FileStorageService.CopyAsync(id, newFileName);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 创建文件版本
    /// </summary>
    [HttpPost("{id:guid}/versions")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileVersionDto>> CreateVersion(
        Guid id,
        IFormFile file,
        [FromForm] string? description = null)
    {
        // 能力判定要排在入参校验之前：反过来的话，缺包的宿主对一个没带文件的请求
        // 回的是「你参数错了」，调用方会去补参数、再收到同一个 400，永远问不出
        // 真正的原因是这台宿主没装这项能力。
        if (FileVersionService == null)
            return Error<FileVersionDto>(WorkspaceMissing, 501);

        if (file == null || file.Length == 0)
        {
            return BadRequest<FileVersionDto>("File is required");
        }

        using var stream = file.OpenReadStream();
        var result = await FileVersionService.CreateVersionAsync(id, stream, description);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取文件版本列表
    /// </summary>
    [HttpGet("{id:guid}/versions")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<IEnumerable<FileVersionDto>>> GetVersions(Guid id)
    {
        if (FileVersionService == null)
            return Error<IEnumerable<FileVersionDto>>(WorkspaceMissing, 501);

        var result = await FileVersionService.GetVersionsAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 恢复指定版本
    /// </summary>
    [HttpPost("{id:guid}/versions/{version}/restore")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileRecordDto>> RestoreVersion(Guid id, int version)
    {
        if (FileVersionService == null)
            return Error<FileRecordDto>(WorkspaceMissing, 501);

        var result = await FileVersionService.RestoreVersionAsync(id, version);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 只读下载指定历史版本（不改变当前版本指针）
    /// </summary>
    [HttpGet("{id:guid}/versions/{version}/download")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<IActionResult> DownloadVersion(Guid id, int version)
    {
        if (FileVersionService == null)
            return WorkspaceMissingResult();

        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var record = recordResult.Data!;

        var contentResult = await FileVersionService.GetVersionContentAsync(id, version);
        if (!contentResult.Succeeded)
        {
            return new NotFoundResult();
        }

        var contentType = record.ContentType ?? "application/octet-stream";
        var fileName = $"{record.OriginalName ?? record.FileName}.v{version}";
        return File(contentResult.Data!, contentType, fileName);
    }

    /// <summary>
    /// 删除指定历史版本（禁止删除当前版本）
    /// </summary>
    [HttpDelete("{id:guid}/versions/{version}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult> DeleteVersion(Guid id, int version)
    {
        if (FileVersionService == null)
            return Error(WorkspaceMissing, 501);

        var result = await FileVersionService.DeleteVersionAsync(id, version);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建分享链接
    /// </summary>
    [HttpPost("{id:guid}/share")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileSharePublicDto>> CreateShare(Guid id, [FromBody] CreateShareRequest request)
    {
        if (FileShareService == null)
            return Error<FileSharePublicDto>(WorkspaceMissing, 501);

        var result = await FileShareService.CreateShareAsync(
            id,
            request.ExpiresAt,
            request.MaxAccessCount,
            request.Password);
        return result.ToApiResult();
    }

    /// <summary>
    /// What a share-link recipient sees before downloading. Anonymous on purpose:
    /// the recipient of an external share link has no account.
    /// </summary>
    /// <remarks>
    /// Returns 404 for a link that is revoked, expired or exhausted - identical to
    /// a token that never existed, so probing reveals nothing. Password-protected
    /// links still preview (the recipient needs to know a password is coming);
    /// the password only gates the bytes.
    /// </remarks>
    [HttpGet("share/{token}/info")]
    [AllowAnonymous]
    public virtual async Task<ApiResult<FileSharePreviewDto>> GetSharePreview(string token)
    {
        if (FileShareService == null)
            return Error<FileSharePreviewDto>(WorkspaceMissing, 501);

        var result = await FileShareService.GetSharePreviewAsync(token);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取分享信息(管理视角,含令牌与计数)。只对分享的创建者 / 对文件有变更权的人可见,其余 404;
    /// 收件人看的是 <c>share/{token}/info</c>。
    /// </summary>
    [HttpGet("share/{token}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileSharePublicDto>> GetShare(string token)
    {
        if (FileShareService == null)
            return Error<FileSharePublicDto>(WorkspaceMissing, 501);

        var result = await FileShareService.GetShareAsync(token);
        return result.ToApiResult();
    }

    /// <summary>
    /// 撤销分享。要求与创建同一份权利(创建者,或对文件有变更权),判定在服务层;否则 404。
    /// </summary>
    [HttpDelete("share/{token}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult> RevokeShare(string token)
    {
        if (FileShareService == null)
            return Error(WorkspaceMissing, 501);

        var result = await FileShareService.RevokeShareAsync(token);
        return result.ToApiResult();
    }

    /// <summary>
    /// 校验分享口令是否正确。**匿名**,且**不消耗访问配额**。
    /// </summary>
    /// <remarks>
    /// 收件人页面靠它在原地反馈口令不对,而不是让浏览器跳去一个 401 页面 ——
    /// 跳走了他就再也回不到那一屏。
    ///
    /// 刻意独立于下载端点:拿下载端点探测会**消耗一次配额**,
    /// 于是 `maxAccessCount = 1` 的链接在真正下载之前就已经用完了。
    /// 口令连错仍照常计入自动停用的闸门(校验逻辑是同一份)。
    /// </remarks>
    [HttpPost("share/{token}/verify")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<bool>> VerifyShareAccess(string token, [FromBody] VerifyShareRequest? request = null)
    {
        if (FileShareService == null)
            return Error<bool>(WorkspaceMissing, 501);

        var result = await FileShareService.ValidateShareAccessAsync(token, request?.Password);
        return result.ToApiResult();
    }

    /// <summary>
    /// 通过分享 token 下载。**匿名**:对外分享的收件人没有账号,这正是它的用途。
    /// </summary>
    /// <remarks>
    /// 令牌本身就是凭据。<c>ValidateShareAccessAsync</c> 校验通过后会把该文件记进请求
    /// 作用域的授予表,后面的取记录 / 取流因此不再要求调用者本人有权 —— 判定仍然全在
    /// 服务层,控制器只负责把请求送进去。
    /// </remarks>
    [HttpGet("share/{token}/download")]
    [AllowAnonymous]
    [SensitiveEndpoint(
        "storage.share-link",
        "Serves file content to an unauthenticated caller who holds a share token. Storage:Share:AllowAnonymous "
        + "defaults to true, so on upgrade every share row already in the database becomes a working public link - "
        + "review the shares list before enabling this in a deployment that never intended external distribution.")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<IActionResult> DownloadByShareToken(string token, [FromQuery] string? password = null)
    {
        if (FileShareService == null)
            return WorkspaceMissingResult();

        // 1) 校验密码/过期/启用（密码校验仍需在此进行）
        var isValidResult = await FileShareService.ValidateShareAccessAsync(token, password);
        if (!isValidResult.Succeeded || !isValidResult.Data)
        {
            return new UnauthorizedResult();
        }

        var shareResult = await FileShareService.GetShareAsync(token);
        if (!shareResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var share = shareResult.Data!;

        // 2) 原子占用一次配额：把"判超限 + 占额"合并为单次原子 DB 操作，消除竞态。
        //    返回 false 表示已超限/已禁用 → 拒绝下载（410 Gone）。
        var consumeResult = await FileShareService.IncrementShareAccessCountAsync(token);
        if (!consumeResult.Succeeded || !consumeResult.Data)
        {
            return new StatusCodeResult(StatusCodes.Status410Gone);
        }

        var fileRecordResult = await FileStorageService.GetRecordAsync(share.FileId);
        if (!fileRecordResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var fileRecord = fileRecordResult.Data!;

        var streamResult = await FileStorageService.GetAsync(share.FileId);
        if (!streamResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var stream = streamResult.Data!;
        return File(stream, fileRecord.ContentType ?? "application/octet-stream", fileRecord.OriginalName ?? fileRecord.FileName);
    }

    /// <summary>
    /// 压缩文件
    /// </summary>
    [HttpPost("compress")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileRecordDto>> Compress([FromBody] CompressRequest request)
    {
        var result = await FileStorageService.CompressAsync(request.FileIds, request.ZipFileName);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 解压文件
    /// </summary>
    [HttpPost("{id:guid}/decompress")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<IEnumerable<FileRecordDto>>> Decompress(Guid id)
    {
        var result = await FileStorageService.DecompressAsync(id);
        return result.Map(items => items.Select(r => r.MapTo<FileRecordDto>())).ToApiResult();
    }

    /// <summary>
    /// 初始化分块上传
    /// </summary>
    [HttpPost("upload/chunk/init")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileUploadSessionDto>> InitiateChunkedUpload([FromBody] InitiateChunkedUploadRequest request)
    {
        if (FileChunkUploadService == null)
            return Error<FileUploadSessionDto>(WorkspaceMissing, 501);

        var result = await FileChunkUploadService.InitiateChunkedUploadAsync(
            request.FileName,
            request.TotalSize,
            request.ChunkSize,
            request.Md5Hash);
        return result.ToApiResult();
    }

    /// <summary>
    /// 上传分块
    /// </summary>
    [HttpPost("upload/chunk/{uploadSessionId:guid}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileChunkDto>> UploadChunk(
        Guid uploadSessionId,
        [FromQuery] int chunkIndex,
        IFormFile chunk)
    {
        if (FileChunkUploadService == null)
            return Error<FileChunkDto>(WorkspaceMissing, 501);

        if (chunk == null || chunk.Length == 0)
        {
            return BadRequest<FileChunkDto>("Chunk file is required");
        }

        using var stream = chunk.OpenReadStream();
        var result = await FileChunkUploadService.UploadChunkAsync(uploadSessionId, chunkIndex, stream);
        return result.ToApiResult();
    }

    /// <summary>
    /// 完成分块上传
    /// </summary>
    [HttpPost("upload/chunk/{uploadSessionId:guid}/complete")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileRecordDto>> CompleteChunkedUpload(
        Guid uploadSessionId,
        [FromBody] CompleteChunkedUploadRequest? request = null)
    {
        if (FileChunkUploadService == null)
            return Error<FileRecordDto>(WorkspaceMissing, 501);

        var result = await FileChunkUploadService.CompleteChunkedUploadAsync(
            uploadSessionId,
            request?.IsTemporary ?? false);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 取消分块上传
    /// </summary>
    [HttpDelete("upload/chunk/{uploadSessionId:guid}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult> CancelChunkedUpload(Guid uploadSessionId)
    {
        if (FileChunkUploadService == null)
            return Error(WorkspaceMissing, 501);

        var result = await FileChunkUploadService.CancelChunkedUploadAsync(uploadSessionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取上传进度
    /// </summary>
    [HttpGet("upload/chunk/{uploadSessionId:guid}/progress")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileUploadProgress>> GetUploadProgress(Guid uploadSessionId)
    {
        if (FileChunkUploadService == null)
            return Error<FileUploadProgress>(WorkspaceMissing, 501);

        var result = await FileChunkUploadService.GetUploadProgressAsync(uploadSessionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 解析 Range 头
    /// </summary>
    private static (long? Start, long? End) ParseRangeHeader(string rangeHeader, long fileSize)
    {
        if (string.IsNullOrEmpty(rangeHeader) || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        var rangeValue = rangeHeader[6..];
        var parts = rangeValue.Split('-');

        if (parts.Length != 2)
        {
            return (null, null);
        }

        long? start = null;
        long? end = null;

        if (!string.IsNullOrEmpty(parts[0]))
        {
            if (long.TryParse(parts[0], out var startValue))
            {
                start = Math.Max(0, startValue);
            }
        }

        if (!string.IsNullOrEmpty(parts[1]))
        {
            if (long.TryParse(parts[1], out var endValue))
            {
                end = Math.Min(fileSize - 1, endValue);
            }
        }
        else if (start.HasValue)
        {
            end = fileSize - 1;
        }

        if (start.HasValue && end.HasValue && start.Value > end.Value)
        {
            return (null, null);
        }

        return (start, end);
    }

    /// <summary>
    /// 两个返回 <see cref="IActionResult"/> 的端点用的 501：它们不走 <c>ApiResult</c>，
    /// 所以得自己把消息写进响应体。
    /// </summary>
    private ObjectResult WorkspaceMissingResult()
        => StatusCode(StatusCodes.Status501NotImplemented, WorkspaceMissing);
}
