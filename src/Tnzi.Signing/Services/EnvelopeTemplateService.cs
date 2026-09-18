namespace Tnzi.Signing.Services;

/// <summary>
/// <see cref="IEnvelopeTemplateService"/> 的默认实现。
/// </summary>
public class EnvelopeTemplateService : ApplicationService, IEnvelopeTemplateService
{
    private readonly IRepository<EnvelopeTemplate, Guid> _templates;
    private readonly IRepository<Field, Guid> _fields;
    private readonly IReadOnlyRepository<Envelope, Guid> _requests;
    private readonly IFileReadAccessProbe _fileAccess;
    private readonly IFileStorageService _files;

    /// <remarks>
    /// <c>fileAccess</c> 是写入侧的归属探针（<see cref="IFileReadAccessProbe"/>）。本模块 <c>[DependsOn(StorageModule)]</c>，
    /// 实现随它注册，故这里是必需依赖而不是可空的 —— 少了 Storage 的应用在解析本服务时就崩在容器里，
    /// 不会静默跳过校验。<c>files</c> 只用来读渲染稿的记录（看它是不是 PDF）。
    /// </remarks>
    public EnvelopeTemplateService(
        IServiceProvider serviceProvider,
        IRepository<EnvelopeTemplate, Guid> templates,
        IRepository<Field, Guid> fields,
        IReadOnlyRepository<Envelope, Guid> requests,
        IFileReadAccessProbe fileAccess,
        IFileStorageService files)
        : base(serviceProvider)
    {
        _templates = Check.NotNull(templates);
        _fields = Check.NotNull(fields);
        _requests = Check.NotNull(requests);
        _fileAccess = Check.NotNull(fileAccess);
        _files = Check.NotNull(files);
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<EnvelopeTemplateListDto>>> GetPagedAsync(
        EnvelopeTemplateQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var q = _templates.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();
            q = q.Where(t => t.Name.ToLower().Contains(keyword) || t.Category.ToLower().Contains(keyword));
        }
        if (!string.IsNullOrWhiteSpace(query.Category))
            q = q.Where(t => t.Category == query.Category);
        if (query.Source.HasValue)
            q = q.Where(t => t.Source == query.Source.Value);
        if (query.IsActive.HasValue)
            q = q.Where(t => t.IsActive == query.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(query.HostEntityType))
        {
            // 不限（空）的模板对每种宿主都可用，所以它们必须留在结果里 —— 否则按宿主
            // 筛选会把通用模板筛没，而那些恰恰是最常用的。
            var host = query.HostEntityType.Trim();
            q = q.Where(t => string.IsNullOrEmpty(t.HostEntityTypes) || t.HostEntityTypes!.Contains(host));
        }

        var paged = await q
            .OrderBy(t => t.Category).ThenBy(t => t.Name)
            .ProjectTo<EnvelopeTemplate, EnvelopeTemplateListDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        // FieldCount 是列表页唯一想知道的字段信息（"这个模板配好了没有"）。
        // 单次分组查询回填，不做 N+1。
        var ids = paged.Items.Select(t => t.Id).ToList();
        if (ids.Count > 0)
        {
            var counts = await _fields.AsNoTracking()
                .Where(f => ids.Contains(f.TemplateId))
                .GroupBy(f => f.TemplateId)
                .Select(g => new { TemplateId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var map = counts.ToDictionary(c => c.TemplateId, c => c.Count);
            foreach (var item in paged.Items)
                item.FieldCount = map.GetValueOrDefault(item.Id);
        }

        return Ok(paged);
    }

    /// <inheritdoc />
    public async Task<Result<EnvelopeTemplateDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _templates.AsNoTracking()
            .Include(t => t.Fields)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return template == null
            ? Fail<EnvelopeTemplateDto>("Template not found.", 404)
            : Ok(ToDto(template));
    }

    /// <inheritdoc />
    public async Task<Result<EnvelopeTemplateDto>> CreateAsync(
        CreateEnvelopeTemplateDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = Validate(input);
        if (!validation.Succeeded)
            return Fail<EnvelopeTemplateDto>(validation.Message ?? "Invalid template.", validation.Code ?? 400);

        var denial = await RejectUnusableFilesAsync(input, cancellationToken);
        if (denial != null)
            return denial;

        var template = new EnvelopeTemplate();
        Apply(template, input);
        template.Version = 1;
        AppendFields(template, input.Fields);

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _templates.InsertAsync(template, cancellationToken: ct);
        }, cancellationToken);

        return await GetAsync(template.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<EnvelopeTemplateDto>> UpdateAsync(
        Guid id, UpdateEnvelopeTemplateDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = Validate(input);
        if (!validation.Succeeded)
            return Fail<EnvelopeTemplateDto>(validation.Message ?? "Invalid template.", validation.Code ?? 400);

        var template = await _templates.AsQueryable()
            .Include(t => t.Fields)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template == null)
            return Fail<EnvelopeTemplateDto>("Template not found.", 404);

        // 更新走同一道门：否则「建模板时用自己的文件、改模板时换成别人的」照样成立。
        // 没换的 id 再问一次也放行 —— 模板名下的文件本就对 signing.template.view 可见（判据 7）。
        var denial = await RejectUnusableFilesAsync(input, cancellationToken);
        if (denial != null)
            return denial;

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // 字段整体重建（硬删 + 重挂）：字段集是一个整体，逐字段 diff 会让结果
            // 取决于操作顺序。已发起的请求拿的是快照，不受影响。
            if (template.Fields.Count > 0)
                await _fields.DeleteManyAsync(template.Fields.ToList(), ct);
            template.Fields.Clear();

            Apply(template, input);
            template.Version += 1;
            AppendFields(template, input.Fields);

            await _templates.UpdateAsync(template, cancellationToken: ct);
        }, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _templates.AsQueryable()
            .Include(t => t.Fields)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template == null)
            return Fail("Template not found.", 404);

        // ★ 引用保护：模板 id 是每份请求快照的出处。删掉它，"这份签好的文件是照哪个
        //   模板出的"就再也答不上来 —— 而那正是归档存在的理由。停用请改 IsActive。
        if (await _requests.AnyAsync(r => r.TemplateId == id, cancellationToken))
        {
            return Fail(
                "This template has been used by at least one signing request and cannot be deleted. Deactivate it instead.",
                409);
        }

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            if (template.Fields.Count > 0)
                await _fields.DeleteManyAsync(template.Fields.ToList(), ct);
            await _templates.DeleteAsync(template, cancellationToken: ct);
        }, cancellationToken);

        return Ok();
    }

    /// <summary>
    /// 请求体里的两个文件 id 能不能写进这份模板。<see langword="null"/> = 可以。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ 引用一个文件 id = 把那份文件<b>发布</b>给这份模板的全部可见者：一条 <c>FileReference</c>
    /// 落库之后，<c>SigningFileReferenceAccessResolver</c> 对 <c>EnvelopeTemplate</c> 名下的文件按
    /// <c>signing.template.view</c> 放行。不问一句归属的话，持 <c>signing.template.create</c> 的人把别人的
    /// 文件 id（HR 档案、他人合同、财务附件）填进模板，保存成功后就能经 <c>files/{id}/download</c>
    /// 取到明文；再把它当渲染稿发起一份请求，令牌就把这份文件原样交给一个匿名的外部收件人。
    /// </para>
    /// <para>
    /// ★ 判据是「这个人本来就读得到它吗」（<see cref="IFileReadAccessProbe"/>），不是「他有没有上传过它」：
    /// 探针刻意不认请求级凭据（URL 签名令牌 / 分享链接授予），一条会过期的分享链接不该被换成一条永久引用。
    /// 「不存在」与「读不到」回答同一句话：分开回答会让这个端点变成「这个 id 存不存在」的探针，
    /// 而实体 ID 是顺序 GUID，可枚举性本来就高。
    /// </para>
    /// <para>
    /// 渲染稿另须是 PDF：收件人端点把它内联交给浏览器、密封器把它交给盖章器，两处都以此为前提。
    /// 原件不限（DOCX 等待转换）。
    /// </para>
    /// </remarks>
    private async Task<Result<EnvelopeTemplateDto>?> RejectUnusableFilesAsync(
        CreateEnvelopeTemplateDto input, CancellationToken cancellationToken)
    {
        foreach (var fileId in new[] { input.SourceFileId, input.RenderedPdfFileId })
        {
            if (fileId is not { } id)
                continue;

            // Guid.Empty 在 [FileField] 那一侧会被静默忽略（不产生引用行）：与其留一个安静的空操作，不如当场说不。
            if (id == Guid.Empty)
                return Fail<EnvelopeTemplateDto>("The file reference is not a valid file id.", 400);

            if (!await _fileAccess.CanReadAsync(id, cancellationToken))
                return Fail<EnvelopeTemplateDto>("That file cannot be used by this template.", 403);
        }

        if (input.RenderedPdfFileId is { } rendered)
        {
            var record = await _files.GetRecordAsync(rendered);
            if (!record.Succeeded || record.Data is null)
                return Fail<EnvelopeTemplateDto>("That file cannot be used by this template.", 403);
            if (!PdfContentType.IsPdf(record.Data.ContentType))
                return Fail<EnvelopeTemplateDto>("The rendered document must be a PDF (application/pdf).", 400);
        }

        return null;
    }

    private static Result Validate(CreateEnvelopeTemplateDto input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Result.Failure("Template name is required.", 400);
        if (input.Name.Trim().Length > 200)
            return Result.Failure("Template name must not exceed 200 characters.", 400);
        if (!Enum.IsDefined(input.Source))
            return Result.Failure("Template source must be Composed or Uploaded.", 400);

        if (input.Source == TemplateSource.Composed && string.IsNullOrWhiteSpace(input.BodyTemplate))
            return Result.Failure("A composed template needs a body.", 400);
        if (input.Source == TemplateSource.Uploaded)
        {
            if (input.SourceFileId is null)
                return Result.Failure("An uploaded template needs its source file.", 400);

            // ★ 渲染稿也必须给。签署请求拿模板的 RenderedPdfFileId 当底稿（发出时再把发起方的值烧上去；
            // EnvelopeService 只对 Composed 模板现排版），缺了它建出来的请求会带着
            // 一份空的 PDF 引用走完整个流程 —— 直到有人去签才发现没有文档可签。
            // 原件已经是 PDF 时两者相同；不是 PDF 就得先转换（框架尚未接线，见下方消息）。
            if (input.RenderedPdfFileId is null)
            {
                return Result.Failure(
                    "An uploaded template needs a rendered PDF. When the source file is already a PDF, "
                    + "send the same file id as RenderedPdfFileId; other formats must be converted to PDF first.",
                    400);
            }
        }

        if (input.PageCount < 1)
            return Result.Failure("PageCount must be at least 1.", 400);

        var fields = input.Fields ?? [];

        // 键在模板内唯一：字段值按键存（快照里也是键），撞键的两个字段会互相覆盖
        // 而且没有任何一处会报错。
        var duplicate = fields
            .GroupBy(f => (f.Key ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            return Result.Failure($"Field key '{duplicate.Key}' appears more than once; keys must be unique within a template.", 400);

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
                return Result.Failure("Every field needs a key.", 400);
            if (!Enum.IsDefined(field.Type))
                return Result.Failure($"Field '{field.Key}': unknown field type.", 400);
            if (!Enum.IsDefined(field.PlacementMode))
                return Result.Failure($"Field '{field.Key}': unknown placement mode.", 400);

            if (field.Type is SigningFieldType.Signature or SigningFieldType.Initials
                && string.IsNullOrWhiteSpace(field.RecipientRole))
            {
                // 空角色对文本字段意味着「发起方预填」，对签名字段则什么都不意味着：它不在任何收件人的
                // 「我的字段」里，没有人被要求交图，密封时按角色找不到图就跳过 —— 成品没有签名，
                // 却照常算哈希、出证书、归档。让它在保存这一步就说不出去。
                return Result.Failure($"Field '{field.Key}': a signature field must name the recipient role that signs it.", 400);
            }

            if (field.PlacementMode == FieldPlacementMode.Anchor)
            {
                if (string.IsNullOrWhiteSpace(field.AnchorText))
                {
                    // 没有锚文本的锚定位字段在密封时会被静默跳过（盖错地方比缺一个签名
                    // 更难发现，所以密封器选择跳过）。让它在保存这一步就说不出去。
                    return Result.Failure($"Field '{field.Key}': anchor placement needs anchor text.", 400);
                }
            }
            else
            {
                if (field.Page < 1)
                    return Result.Failure($"Field '{field.Key}': page must be 1 or greater.", 400);
                if (field.Page > input.PageCount)
                    return Result.Failure($"Field '{field.Key}': page {field.Page} is beyond the template's {input.PageCount} page(s).", 400);
            }

            // 坐标一律归一化 0-1。落在框外的字段会被盖到页面外面 —— 那是一个看不见
            // 但确实缺失的签名。
            if (field.X < 0 || field.X > 1 || field.Y < 0 || field.Y > 1)
                return Result.Failure($"Field '{field.Key}': X and Y are normalized and must be between 0 and 1.", 400);
            if (field.W < 0 || field.W > 1 || field.H < 0 || field.H > 1)
                return Result.Failure($"Field '{field.Key}': W and H are normalized and must be between 0 and 1.", 400);
            if (field.X + field.W > 1.0001m || field.Y + field.H > 1.0001m)
                return Result.Failure($"Field '{field.Key}': the box runs off the page.", 400);

            if (field.FontSize is <= 0)
                return Result.Failure($"Field '{field.Key}': font size must be positive.", 400);
        }

        return Result.Success();
    }

    private static void Apply(EnvelopeTemplate template, CreateEnvelopeTemplateDto input)
    {
        template.Name = input.Name.Trim();
        template.Category = input.Category?.Trim() ?? string.Empty;
        template.Source = input.Source;
        template.HostEntityTypes = string.IsNullOrWhiteSpace(input.HostEntityTypes) ? null : input.HostEntityTypes.Trim();
        template.BodyTemplate = input.BodyTemplate?.Trim() ?? string.Empty;
        template.SourceFileId = input.SourceFileId;
        template.SourceFileName = input.SourceFileName;
        template.RenderedPdfFileId = input.RenderedPdfFileId;
        template.PageCount = input.PageCount;
        template.RequiresWetSignature = input.RequiresWetSignature;
        template.IsActive = input.IsActive;
    }

    private static void AppendFields(EnvelopeTemplate template, List<TemplateFieldInputDto> fields)
    {
        var order = 0;
        foreach (var input in fields ?? [])
        {
            template.Fields.Add(new Field
            {
                Key = input.Key.Trim(),
                Label = string.IsNullOrWhiteSpace(input.Label) ? input.Key.Trim() : input.Label.Trim(),
                Type = input.Type,
                RecipientRole = string.IsNullOrWhiteSpace(input.RecipientRole) ? null : input.RecipientRole.Trim(),
                Binding = string.IsNullOrWhiteSpace(input.Binding) ? null : input.Binding.Trim(),
                Required = input.Required,
                PlacementMode = input.PlacementMode,
                AnchorText = string.IsNullOrWhiteSpace(input.AnchorText) ? null : input.AnchorText,
                Page = input.Page < 1 ? 1 : input.Page,
                X = input.X,
                Y = input.Y,
                W = input.W,
                H = input.H,
                FontSize = input.FontSize,
                SortOrder = input.SortOrder != 0 ? input.SortOrder : order,
            });
            order++;
        }
    }

    private static EnvelopeTemplateDto ToDto(EnvelopeTemplate template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        Category = template.Category,
        Source = template.Source,
        PageCount = template.PageCount,
        FieldCount = template.Fields.Count,
        RequiresWetSignature = template.RequiresWetSignature,
        IsActive = template.IsActive,
        Version = template.Version,
        CreationTime = template.CreationTime,
        HostEntityTypes = template.HostEntityTypes,
        BodyTemplate = template.BodyTemplate,
        SourceFileId = template.SourceFileId,
        SourceFileName = template.SourceFileName,
        RenderedPdfFileId = template.RenderedPdfFileId,
        Fields = template.Fields
            .OrderBy(f => f.SortOrder)
            .Select(f => new TemplateFieldDto
            {
                Id = f.Id,
                Key = f.Key,
                Label = f.Label,
                Type = f.Type,
                RecipientRole = f.RecipientRole,
                Binding = f.Binding,
                Required = f.Required,
                PlacementMode = f.PlacementMode,
                AnchorText = f.AnchorText,
                Page = f.Page,
                X = f.X,
                Y = f.Y,
                W = f.W,
                H = f.H,
                FontSize = f.FontSize,
                SortOrder = f.SortOrder,
            })
            .ToList(),
    };
}
