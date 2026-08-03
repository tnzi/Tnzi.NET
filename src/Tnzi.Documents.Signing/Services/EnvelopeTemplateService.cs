namespace Tnzi.Documents.Signing.Services;

/// <summary>
/// <see cref="IEnvelopeTemplateService"/> 的默认实现。
/// </summary>
public class EnvelopeTemplateService : ApplicationService, IEnvelopeTemplateService
{
    private readonly IRepository<EnvelopeTemplate, Guid> _templates;
    private readonly IRepository<Field, Guid> _fields;
    private readonly IReadOnlyRepository<Envelope, Guid> _requests;

    public EnvelopeTemplateService(
        IServiceProvider serviceProvider,
        IRepository<EnvelopeTemplate, Guid> templates,
        IRepository<Field, Guid> fields,
        IReadOnlyRepository<Envelope, Guid> requests)
        : base(serviceProvider)
    {
        _templates = Check.NotNull(templates);
        _fields = Check.NotNull(fields);
        _requests = Check.NotNull(requests);
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
        if (input.Source == TemplateSource.Uploaded && input.SourceFileId is null)
            return Result.Failure("An uploaded template needs its source file.", 400);

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
