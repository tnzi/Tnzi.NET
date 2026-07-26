namespace Tnzi.Finance.Services;

/// <summary>
/// 单据附件服务
/// </summary>
public class DocumentAttachmentService : ApplicationService, IDocumentAttachmentService
{
    private readonly IRepository<DocumentAttachment, Guid> _repository;
    private readonly FinanceOptions _options;

    public DocumentAttachmentService(
        IServiceProvider serviceProvider,
        IRepository<DocumentAttachment, Guid> repository,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _options = Check.NotNull(options).Value;
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
