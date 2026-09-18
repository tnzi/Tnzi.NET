namespace Tnzi.Finance.Services;

/// <summary>
/// 单据附件服务
/// </summary>
/// <remarks>
/// <see cref="IFileReadAccessProbe"/> 可选注入（契约在核心 <c>Tnzi</c> 程序集，实现随 <c>Tnzi.Storage</c> 注册，
/// Finance 核心仍零 Storage 引用）：登记附件前问一句「这个人本来就读得到这份文件吗」。
/// 未加载 Storage 时为 null，<see cref="AttachAsync"/> 答 501 而不是跳过 —— 「跳过校验」与「校验通过」
/// 在接口上完全一致，而 <c>[FileField]</c> 的引用登记本来就是存储模块的机制。
/// </remarks>
public class DocumentAttachmentService : ApplicationService, IDocumentAttachmentService
{
    private readonly IRepository<DocumentAttachment, Guid> _repository;
    private readonly FinanceOptions _options;
    private readonly IFileReadAccessProbe? _fileAccess;

    public DocumentAttachmentService(
        IServiceProvider serviceProvider,
        IRepository<DocumentAttachment, Guid> repository,
        IOptionsSnapshot<FinanceOptions> options,
        IFileReadAccessProbe? fileAccess = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _options = Check.NotNull(options).Value;
        _fileAccess = fileAccess;
    }

    public async Task<Result<List<DocumentAttachmentDto>>> ListAsync(string sourceType, string sourceId, CancellationToken cancellationToken = default)
    {
        var keyResult = NormalizeKey(sourceType, sourceId);
        if (!keyResult.Succeeded)
            return Fail<List<DocumentAttachmentDto>>(keyResult.Message!, keyResult.Code ?? 400);
        var (type, id) = keyResult.Data;

        var list = await _repository.AsNoTracking()
            .Where(a => a.SourceType == type && a.SourceId == id)
            .OrderBy(a => a.CreationTime)
            .Select(a => new DocumentAttachmentDto
            {
                Id = a.Id,
                SourceType = a.SourceType,
                SourceId = a.SourceId,
                FileId = a.FileId,
                FileName = a.FileName,
                ContentType = a.ContentType,
                FileSize = a.FileSize,
                Caption = a.Caption,
                CreatorId = a.CreatorId,
                CreationTime = a.CreationTime
            })
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    public async Task<Result<DocumentAttachmentDto>> AttachAsync(
        string sourceType, string sourceId, CreateDocumentAttachmentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var keyResult = NormalizeKey(sourceType, sourceId);
        if (!keyResult.Succeeded)
            return Fail<DocumentAttachmentDto>(keyResult.Message!, keyResult.Code ?? 400);
        var (type, id) = keyResult.Data;

        if (input.FileId == Guid.Empty)
            return Fail<DocumentAttachmentDto>("A file is required.", 400);

        // ★★★ 登记一个文件 id = 把那份文件**发布**给这张单据的全部可见者：FileId 是 [FileField]，
        // 落库即登记一条 FileReference，而 FinanceFileReferenceAccessResolver 对 DocumentAttachment 名下的
        // 文件只问 finance.attachment.view。不问一句归属，持 finance.attachment.create 的人把任意 fileId
        // 挂到任意单据键上（单据类型是开放词汇，单据本身不必存在），那份文件就成了他永久可读的。
        // 判据是「这个人本来就读得到它吗」，不认请求级凭据（分享链接 / 签名 URL），见 IFileReadAccessProbe。
        var denial = await RejectFileReferenceAsync(input.FileId, cancellationToken);
        if (denial != null)
            return denial;

        // 内容类型白名单：空 = 不限（多数部署不想管这件事，那就别逼他们配）。
        var allowed = _options.AllowedAttachmentContentTypes;
        if (allowed is { Length: > 0 } && !string.IsNullOrWhiteSpace(input.ContentType)
            && !allowed.Any(a => string.Equals(a, input.ContentType, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail<DocumentAttachmentDto>($"Files of type '{input.ContentType}' cannot be attached here.", 400);
        }

        var count = await _repository.AsNoTracking().CountAsync(a => a.SourceType == type && a.SourceId == id, cancellationToken);
        if (count >= _options.MaxAttachmentsPerDocument)
            return Fail<DocumentAttachmentDto>($"This document already has the maximum of {_options.MaxAttachmentsPerDocument} attachments.", 409);

        // 同一个文件挂两次多半是重复点击，而不是真想挂两份。
        if (await _repository.AsNoTracking().AnyAsync(a => a.SourceType == type && a.SourceId == id && a.FileId == input.FileId, cancellationToken))
            return Fail<DocumentAttachmentDto>("That file is already attached to this document.", 409);

        var attachment = new DocumentAttachment
        {
            SourceType = type,
            SourceId = id,
            FileId = input.FileId,
            FileName = string.IsNullOrWhiteSpace(input.FileName) ? input.FileId.ToString("N")[..8] : input.FileName.Trim(),
            ContentType = input.ContentType,
            FileSize = input.FileSize < 0 ? 0 : input.FileSize,
            Caption = string.IsNullOrWhiteSpace(input.Caption) ? null : input.Caption.Trim(),
        };

        await _repository.InsertAsync(attachment, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Ok(new DocumentAttachmentDto
        {
            Id = attachment.Id,
            SourceType = attachment.SourceType,
            SourceId = attachment.SourceId,
            FileId = attachment.FileId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            FileSize = attachment.FileSize,
            Caption = attachment.Caption,
            CreatorId = attachment.CreatorId,
            CreationTime = attachment.CreationTime
        });
    }

    public async Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var attachment = await _repository.AsQueryable(true).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (attachment == null)
            return Fail("Attachment not found.", 404);

        // 软删：谁在什么时候把它摘下来的，同样是要留痕的事。文件本身的去留交给
        // Storage 的引用跟踪——这里不删文件，别的单据可能还挂着同一个。
        await _repository.DeleteAsync(attachment, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    public async Task<Result<Dictionary<string, int>>> CountBySourceAsync(
        string sourceType, IReadOnlyCollection<string> sourceIds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
            return Fail<Dictionary<string, int>>("A document type is required.", 400);
        if (sourceIds == null || sourceIds.Count == 0)
            return Ok(new Dictionary<string, int>());

        var type = sourceType.Trim();
        var ids = sourceIds.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();

        var counts = await _repository.AsNoTracking()
            .Where(a => a.SourceType == type && ids.Contains(a.SourceId))
            .GroupBy(a => a.SourceId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        return Ok(counts);
    }

    /// <summary>
    /// 这个文件 id 能不能被当前用户挂到单据上。<see langword="null"/> = 可以。
    /// </summary>
    /// <remarks>
    /// 存储模块缺席时拒绝，不是跳过：501 而不是 503，这不是暂时性故障，重试永远不会好。
    /// 「读不到」与「不存在」回答同一句话：分开回答会让这个端点变成「这个文件 id 存不存在」的探针，
    /// 而实体 ID 是顺序 GUID，可枚举性本来就高。
    /// </remarks>
    private async Task<Result<DocumentAttachmentDto>?> RejectFileReferenceAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (_fileAccess == null)
            return Fail<DocumentAttachmentDto>("Attaching files needs the storage module. Load Tnzi.Storage.", 501);

        if (!await _fileAccess.CanReadAsync(fileId, cancellationToken))
            return Fail<DocumentAttachmentDto>("That file cannot be attached to this document.", 403);

        return null;
    }

    /// <summary>
    /// 校验并归一化单据键。
    /// </summary>
    /// <remarks>
    /// **刻意不校验 sourceType 属于某个封闭枚举**：消费应用经
    /// <c>ILedgerPostingService</c> 写自己的来源令牌，把它关进枚举就等于把附件
    /// 功能对自定义单据关死。代价是可能留下指向已删单据的孤儿行，与
    /// <c>JournalLine.SourceType</c> 的既有取舍一致。
    /// </remarks>
    private Result<(string Type, string Id)> NormalizeKey(string sourceType, string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
            return Fail<(string, string)>("A document type is required.", 400);
        if (string.IsNullOrWhiteSpace(sourceId))
            return Fail<(string, string)>("A document id is required.", 400);

        return Ok((sourceType.Trim(), sourceId.Trim()));
    }
}
