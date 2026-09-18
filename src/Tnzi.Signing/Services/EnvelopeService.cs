namespace Tnzi.Signing.Services;

/// <inheritdoc cref="IEnvelopeService" />
public partial class EnvelopeService : ApplicationService, IEnvelopeService
{
    private readonly IRepository<Envelope, Guid> _requests;
    private readonly IRepository<Signer, Guid> _recipients;
    private readonly IRepository<FieldValue, Guid> _values;
    private readonly IReadOnlyRepository<EnvelopeTemplate, Guid> _templates;
    private readonly IReadOnlyRepository<Field, Guid> _fields;
    private readonly IMergeSourceRegistry _registry;
    private readonly SigningSealer _sealer;
    private readonly SigningCertificateBuilder _certificates;
    private readonly ComposedDocumentRenderer _composer;
    private readonly IFileStorageService _files;

    /// <summary>
    /// 本次请求内的文件读取授予。签署令牌校验通过后把这份请求的文档 id 写进去，
    /// 让 <c>Tnzi.Storage</c> 的读取判定在同一个请求里放行 —— 收件人是匿名的，
    /// 少了这一步，密封读不到渲染稿、收件人也取不到自己正在签的文档。
    /// </summary>
    private readonly IFileAccessGrantContext _grants;

    /// <summary>
    /// 当前租户。匿名收件人没有租户上下文，令牌解析得出后把这次请求切进那一行的租户（见收件人面）。
    /// <c>Tnzi.EFCore</c> 注册它，本模块 <c>[DependsOn(EFCoreModule)]</c>，故是必需依赖。
    /// </summary>
    private readonly ICurrentTenant _currentTenant;

    public EnvelopeService(
        IServiceProvider serviceProvider,
        IRepository<Envelope, Guid> requests,
        IRepository<Signer, Guid> recipients,
        IRepository<FieldValue, Guid> values,
        IReadOnlyRepository<EnvelopeTemplate, Guid> templates,
        IReadOnlyRepository<Field, Guid> fields,
        IMergeSourceRegistry registry,
        SigningSealer sealer,
        SigningCertificateBuilder certificates,
        ComposedDocumentRenderer composer,
        IFileStorageService files,
        IFileAccessGrantContext grants,
        ICurrentTenant currentTenant)
        : base(serviceProvider)
    {
        _requests = Check.NotNull(requests);
        _recipients = Check.NotNull(recipients);
        _values = Check.NotNull(values);
        _templates = Check.NotNull(templates);
        _fields = Check.NotNull(fields);
        _registry = Check.NotNull(registry);
        _sealer = Check.NotNull(sealer);
        _certificates = Check.NotNull(certificates);
        _composer = Check.NotNull(composer);
        _files = Check.NotNull(files);
        _grants = Check.NotNull(grants);
        _currentTenant = Check.NotNull(currentTenant);
    }

    /// <inheritdoc />
    public async Task<Result<EnvelopeDto>> CreateAsync(CreateEnvelopeDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (input.Recipients is not { Count: > 0 })
            return Fail<EnvelopeDto>("At least one recipient is required.", 400);

        var roleCheck = CheckRecipientRoles(input.Recipients);
        if (roleCheck != null)
            return Fail<EnvelopeDto>(roleCheck.Message!, roleCheck.Code ?? 400);

        var template = await _templates.GetAsync(input.TemplateId, cancellationToken);
        if (template == null)
            return Fail<EnvelopeDto>("Template not found.", 404);

        if (!template.IsActive)
        {
            // ★ 停用必须真的挡住新请求 —— 否则「停用」只是列表里的一个灰标签，而删除那侧
            //   正是让人「改用停用」的（模板一旦被引用就不许删）。已发起的请求不受影响：
            //   它们拿的是快照。
            return Fail<EnvelopeDto>(
                "This template is deactivated and cannot be used to start a new signing request.", 409);
        }

        if (template.RequiresWetSignature)
        {
            // 这类文书按法域要求必须手写签名。让它在发起这一步就被拦下，
            // 好过让人走完整个流程才发现这份不能用电子签。
            return Fail<EnvelopeDto>(
                "This template requires a wet signature and cannot be signed electronically.", 409);
        }

        var templateFields = await _fields.ToListAsync(f => f.TemplateId == template.Id, cancellationToken);

        // ★ 此刻就冻结：模板之后被改或被停用，都与这份已发起的文档无关。
        var snapshot = new SigningSnapshot
        {
            TemplateId = template.Id,
            TemplateVersion = template.Version,
            TemplateName = template.Name,
            Fields = templateFields.OrderBy(f => f.SortOrder).Select(SnapshotField.From).ToList(),
        };

        var coverage = CheckFieldRoleCoverage(snapshot, input.Recipients);
        if (coverage != null)
            return Fail<EnvelopeDto>(coverage.Message!, coverage.Code ?? 400);

        // 合并变量与预填在落库、排版之前就解析并过上限：越界是 400 不是写列时的数据库异常。
        var initialValues = await ResolveInitialValuesAsync(snapshot, input, cancellationToken);
        var lengthCheck = CheckInitialValueLengths(snapshot, initialValues);
        if (lengthCheck != null)
            return Fail<EnvelopeDto>(lengthCheck.Message!, lengthCheck.Code ?? 400);

        var title = string.IsNullOrWhiteSpace(input.Title) ? template.Name : input.Title.Trim();
        var renderedPdfFileId = template.RenderedPdfFileId;

        if (template.Source == TemplateSource.Composed)
        {
            // ★ Composed 模板**每份请求各排一次版**，不是模板存一份渲染稿：正文里带合并
            //   变量，不同宿主记录排出来的分页可能不同，字段落点也就不同。共用一份渲染稿
            //   等于让第二份文档的签名框停在第一份文档的位置上。
            var composed = await RenderComposedAsync(template, snapshot, input, title, cancellationToken);
            if (!composed.Succeeded)
                return Fail<EnvelopeDto>(composed.Message ?? "The document could not be rendered.", composed.Code ?? 500);

            renderedPdfFileId = composed.Data.FileId;
            snapshot = composed.Data.Snapshot;
        }

        var request = new Envelope
        {
            HostEntityType = input.HostEntityType,
            HostEntityId = input.HostEntityId,
            TemplateId = template.Id,
            Title = title,
            TemplateSnapshotJson = snapshot.ToJson(),
            RenderedPdfFileId = renderedPdfFileId,
            IsSequential = input.IsSequential,
            Status = EnvelopeStatus.Draft,
            ExpiresAt = DateTime.UtcNow.AddDays(Math.Max(1, input.ExpiresInDays)),
            SentByUserId = CurrentUser?.Id,
            SentByName = CurrentUser?.UserName,
        };

        await _requests.InsertAsync(request, cancellationToken: cancellationToken);
        await FlushAsync(cancellationToken);

        var order = 1;
        foreach (var r in input.Recipients)
        {
            await _recipients.InsertAsync(new Signer
            {
                RequestId = request.Id,
                Role = r.Role.Trim(),
                Name = r.Name,
                Email = r.Email,
                Order = order++,
                // 令牌在 SendAsync 才签发：草稿阶段不该存在可用的签署链接。
                // null 而非空串——空串会撞上 TokenHash 的唯一索引（见实体上的说明）。
                TokenHash = null,
                Status = SigningRecipientStatus.Pending,
            }, cancellationToken: cancellationToken);
        }

        await StoreValuesAsync(request.Id, initialValues, null, cancellationToken);
        await FlushAsync(cancellationToken);

        return await GetAsync(request.Id, cancellationToken);
    }

    /// <summary>
    /// 排版 Composed 模板：解析合并变量 → 渲染 → 把就地捕获的落点写回快照。
    /// </summary>
    /// <remarks>
    /// 捕获到的落点一律以 <see cref="FieldPlacementMode.Absolute"/> 回写 —— 我们刚刚亲手把它
    /// 排在那里，再让密封器去搜一遍锚文本是把已知的东西丢掉再猜回来。正文里没出现的字段
    /// 保持模板上原本的定位方式不动。
    /// </remarks>
    private async Task<Result<(Guid FileId, SigningSnapshot Snapshot)>> RenderComposedAsync(
        EnvelopeTemplate template,
        SigningSnapshot snapshot,
        CreateEnvelopeDto input,
        string title,
        CancellationToken cancellationToken)
    {
        var merge = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var provider = _registry.FindProvider(input.HostEntityType);
        if (provider != null && input.HostEntityId is { } hostId)
        {
            foreach (var (key, value) in await provider.ResolveAsync(hostId, cancellationToken))
                merge[key] = value;
        }

        ComposedRenderResult rendered;
        try
        {
            rendered = _composer.Render(title, template.BodyTemplate, merge, snapshot.Fields);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Rendering composed template {TemplateId} failed.", template.Id);
            return Result<(Guid, SigningSnapshot)>.Failure("The document could not be rendered.", 500);
        }

        using var stream = new MemoryStream(rendered.Pdf, writable: false);
        var saved = await _files.SaveAsync($"{SafeName(title)}.pdf", stream);
        if (!saved.Succeeded || saved.Data is null)
            return Result<(Guid, SigningSnapshot)>.Failure("The rendered document could not be stored.", 500);

        var updated = snapshot with
        {
            Fields = snapshot.Fields.Select(f =>
                rendered.Placements.TryGetValue(f.Key, out var p)
                    ? f with
                    {
                        PlacementMode = FieldPlacementMode.Absolute,
                        Page = p.Page,
                        X = p.X,
                        Y = p.Y,
                        W = p.W,
                        H = p.H,
                    }
                    : f).ToList(),
        };

        return Result<(Guid, SigningSnapshot)>.Success((saved.Data.Id, updated));
    }

    /// <summary>
    /// 一角色一人。
    /// </summary>
    /// <remarks>
    /// 签名图与字段值都按<b>角色</b>寻址：密封器按 <c>Role</c> 分组取签名（同角色只取第一张），
    /// 字段值按键存（同角色第二人的提交覆盖第一人）。放两个人进同一个角色，成品上只会有一个人的签名、
    /// 字段值是后交的那一份，而完成证书给两个人各写一行 Signed —— 成品与证据链矛盾，且没有任何日志。
    /// 要支持同角色多人，签名与字段值都得改按收件人寻址（快照与 FieldValue 的唯一键都要变），
    /// 在那之前这条是显式契约。角色也是 <c>Signer.Role</c> 的必填列，空白在这里拦而不是等数据库报错。
    /// </remarks>
    private static Result? CheckRecipientRoles(IEnumerable<CreateSignerDto> recipients)
    {
        var roles = recipients.Select(r => r.Role?.Trim() ?? string.Empty).ToList();
        if (roles.Any(string.IsNullOrEmpty))
            return Result.Failure("Every recipient needs a role.", 400);

        var duplicate = roles
            .GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            return Result.Failure(
                $"Recipient role '{duplicate.Key}' is used more than once; each role must map to exactly one recipient.", 400);
        }

        return null;
    }

    /// <summary>
    /// 字段指名的角色必须真的有人持有。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="CheckRecipientRoles"/> 是同一条契约的另一面：那边保证收件人之间角色不撞，这边保证字段指向的角色
    /// 在收件人里。少了这一步，一个拼错的角色（<c>Cient</c>）或模板里多出来的角色（<c>Witness</c>）会让那个签名字段
    /// 不在任何人的份内 —— <c>SubmitAsync</c> 只对本人角色的字段要签名，密封器对找不到签名的字段只记一行 Warning，
    /// 于是一份签名位空白的成品照样被密封、算哈希、出完成证书。
    /// 签名类字段无论必填与否都要有人（没人签的签名框就是成品上的一块空白）；必填字段也要；
    /// 非必填的非签名字段留白是它自己的选择，与非必填的发起方字段同一口径。角色为空的字段是发起方字段，不指向任何人。
    /// </remarks>
    private static Result? CheckFieldRoleCoverage(SigningSnapshot snapshot, IEnumerable<CreateSignerDto> recipients)
    {
        var held = recipients
            .Select(r => r.Role.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var uncovered = snapshot.Fields
            .Where(f => f.IsSignatureLike || f.Required)
            .Where(f => f.RecipientRole is { } role && !string.IsNullOrWhiteSpace(role) && !held.Contains(role.Trim()))
            .Select(f => $"'{f.Key}' (role '{f.RecipientRole!.Trim()}')")
            .ToList();
        if (uncovered.Count > 0)
        {
            return Result.Failure(
                $"These fields are addressed to roles no recipient holds: {string.Join(", ", uncovered)}.", 400);
        }

        return null;
    }

    private static string SafeName(string title)
    {
        var safe = string.IsNullOrWhiteSpace(title) ? "document" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return safe.Length > 120 ? safe[..120] : safe;
    }

    /// <summary>合并变量 + 发起方预填，合成初始取值。</summary>
    private async Task<Dictionary<string, string?>> ResolveInitialValuesAsync(
        SigningSnapshot snapshot,
        CreateEnvelopeDto input,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        // 宿主记录的合并变量。provider 未注册或宿主为空都不是错误 ——
        // 一份独立文档本来就没有可合并的记录。
        var provider = _registry.FindProvider(input.HostEntityType);
        if (provider != null && input.HostEntityId is { } hostId)
        {
            var resolved = await provider.ResolveAsync(hostId, cancellationToken);
            foreach (var field in snapshot.Fields)
            {
                if (field.Binding is not { Length: > 0 } binding) continue;
                // ★ provider 省略某个键 = "这份记录没有这个信息"，与"值是空串"不同。
                //   这里也照此处理：不写进去，好让 SendAsync 的必填校验能在发出前拦下它。
                if (resolved.TryGetValue(binding, out var v) && v != null)
                    values[field.Key] = Convert.ToString(v, CultureInfo.InvariantCulture);
            }
        }

        // 发起方预填覆盖合并结果（人是权威的）。
        foreach (var (key, value) in input.PrefilledValues ?? [])
            values[key] = value;

        return values;
    }

    /// <summary>
    /// 发起方那侧的取值也受 <see cref="SigningLimits.MaxFieldValueLength"/> 约束。
    /// </summary>
    /// <remarks>
    /// 列上限是为匿名提交加的，但列不认来源：合并变量与预填写的是同一列，SQLite 看不见宽度而
    /// SQL Server / PostgreSQL 会把越界变成 <c>DbUpdateException</c>（500）。这里按字段指名 400，
    /// 与 <c>SubmitAsync</c> 同一句话；刻意不截断 —— 悄悄截掉一段条款比拒绝更糟。
    /// 预填的键可能不对应任何快照字段，拿不到标签时用键指名。
    /// </remarks>
    private static Result? CheckInitialValueLengths(SigningSnapshot snapshot, IReadOnlyDictionary<string, string?> values)
    {
        var labels = snapshot.Fields.ToDictionary(f => f.Key, f => f.Label, StringComparer.Ordinal);
        var tooLong = values
            .Where(kv => kv.Value is { Length: > SigningLimits.MaxFieldValueLength })
            .Select(kv => labels.TryGetValue(kv.Key, out var label) && !string.IsNullOrWhiteSpace(label) ? label : kv.Key)
            .ToList();
        if (tooLong.Count > 0)
        {
            return Result.Failure(
                $"The value of '{string.Join("', '", tooLong)}' is too long (maximum {SigningLimits.MaxFieldValueLength} characters).", 400);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<IssuedSigningLink>>> SendAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _requests.GetAsync(requestId, cancellationToken);
        if (request == null)
            return Fail<IReadOnlyList<IssuedSigningLink>>("Signing request not found.", 404);
        if (request.Status != EnvelopeStatus.Draft)
            return Fail<IReadOnlyList<IssuedSigningLink>>("Only a draft request can be sent.", 409);
        if (request.RenderedPdfFileId is null)
            return Fail<IReadOnlyList<IssuedSigningLink>>("This request has no document to send.", 409);

        var recipients = await LoadRecipientsAsync(requestId, cancellationToken);
        if (recipients.Count == 0)
            return Fail<IReadOnlyList<IssuedSigningLink>>("This request has no recipients.", 409);

        var snapshot = SigningSnapshot.FromJson(request.TemplateSnapshotJson);
        if (snapshot == null)
            return Fail<IReadOnlyList<IssuedSigningLink>>("This request's template snapshot cannot be read.", 409);

        // ★ 发起方负责的必填字段（角色为空：值来自合并变量或预填）在这里拦，因为别处拦不到：
        //   SubmitAsync 的必填校验只看收件人自己角色的字段，而这类字段永远不在任何人的 mine 里。
        //   provider 按契约省略解析不出的键，CreateAsync 照此不写值 —— 那个「省略」存在的全部理由
        //   就是让这一步能拦下一份不完整的合并，否则缺值的合同会照常发出、签完，密封时该处留白。
        var values = await LoadValuesAsync(requestId, cancellationToken);
        var missing = snapshot.Fields
            .Where(f => f.Required && !f.IsSignatureLike && string.IsNullOrWhiteSpace(f.RecipientRole))
            .Where(f => string.IsNullOrWhiteSpace(values.GetValueOrDefault(f.Key)))
            .Select(f => f.Label)
            .ToList();
        if (missing.Count > 0)
        {
            return Fail<IReadOnlyList<IssuedSigningLink>>(
                $"These required fields have no value yet: {string.Join(", ", missing)}.", 409);
        }

        // ★ 渲染合并稿：把发起方负责的字段值烧进一份本信封自有的渲染稿。签署人在签的时候要看得见
        //   将被密封的内容 —— 收件人载荷只含本人角色的字段，这些值若等到密封才盖，签的是一份
        //   价格处空白的文档而成品上有价格。Uploaded 与 Composed 同一处理（Composed 的正文 {{var}}
        //   早在排版时就在纸面上，[[field]] 绑定字段与 Uploaded 是同一套机制）。
        //   读模板渲染稿要授予：调用方是管理端（控制器门 signing.request.update），与重新密封同一理由。
        GrantDocumentAccess(request);
        var prefill = await _sealer.PrefillAsync(request, snapshot, values, cancellationToken);
        if (!prefill.Succeeded || prefill.Data is null)
        {
            // 失败就不发：发一份「文档里没有价格、成品上有」的信封正是这一步要防的事。
            return Fail<IReadOnlyList<IssuedSigningLink>>(
                prefill.Message ?? "The document could not be rendered.", prefill.Code ?? 500);
        }
        if (prefill.Data.FileId is { } prefilledId)
        {
            request.RenderedPdfFileId = prefilledId;
            request.TemplateSnapshotJson = (snapshot with { PrefilledKeys = prefill.Data.Keys }).ToJson();
            _grants.Grant(prefilledId);
        }

        var issued = new List<IssuedSigningLink>(recipients.Count);
        foreach (var recipient in recipients)
        {
            var token = OneTimeToken.Create();
            recipient.TokenHash = OneTimeToken.Hash(token);
            // 顺序签署时只有第一位处于"已送达"，其余仍在排队。
            recipient.Status = !request.IsSequential || recipient.Order == recipients[0].Order
                ? SigningRecipientStatus.Sent
                : SigningRecipientStatus.Pending;
            if (recipient.Status == SigningRecipientStatus.Sent)
                recipient.SentAt = DateTime.UtcNow;

            await _recipients.UpdateAsync(recipient, cancellationToken: cancellationToken);
            // ★ 明文只在这一刻存在于内存里；库里从此只有哈希。
            issued.Add(new IssuedSigningLink(recipient.Id, recipient.Name, recipient.Email, token, request.TenantId));
        }

        request.Status = EnvelopeStatus.Sent;
        await _requests.UpdateAsync(request, cancellationToken: cancellationToken);
        await FlushAsync(cancellationToken);

        return Ok<IReadOnlyList<IssuedSigningLink>>(issued);
    }

    /// <inheritdoc />
    public async Task<Result> VoidAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _requests.GetAsync(requestId, cancellationToken);
        if (request == null) return Fail("Signing request not found.", 404);

        if (request.Status == EnvelopeStatus.Completed)
        {
            // 已密封的文档不能作废：它已经是一份签成的文件，撤销它是业务动作
            // （另立一份撤销协议），不是把状态改回去。
            return Fail("A completed request cannot be voided.", 409);
        }

        request.Status = EnvelopeStatus.Voided;
        await _requests.UpdateAsync(request, cancellationToken: cancellationToken);

        // ★ 作废是真的吊销：清掉每个收件人的令牌哈希，链接从此解析不出（404）。
        //   只改状态的话，一份发错人的请求收不回来 —— 持链接者仍能取件、取文档。
        //   与「草稿阶段 TokenHash 为 null」同一不变量：没有可用的签署链接就是 null。
        foreach (var recipient in await LoadRecipientsAsync(requestId, cancellationToken))
        {
            if (recipient.TokenHash is null) continue;
            recipient.TokenHash = null;
            await _recipients.UpdateAsync(recipient, cancellationToken: cancellationToken);
        }

        await FlushAsync(cancellationToken);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<EnvelopeDto>> GetAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _requests.GetAsync(requestId, cancellationToken);
        if (request == null) return Fail<EnvelopeDto>("Signing request not found.", 404);

        var recipients = await LoadRecipientsAsync(requestId, cancellationToken);
        return Ok(new EnvelopeDto
        {
            Id = request.Id,
            Title = request.Title,
            HostEntityType = request.HostEntityType,
            HostEntityId = request.HostEntityId,
            Status = EnvelopeExpiry.Derive(request.Status, request.ExpiresAt, DateTime.UtcNow),
            IsSequential = request.IsSequential,
            ExpiresAt = request.ExpiresAt,
            CompletedAt = request.CompletedAt,
            FinalPdfFileId = request.FinalPdfFileId,
            Sha256 = request.Sha256,
            CompletionCertificateFileId = request.CompletionCertificateFileId,
            Recipients = recipients.Select(r => new SignerDto
            {
                Id = r.Id,
                Role = r.Role,
                Name = r.Name,
                Email = r.Email,
                Order = r.Order,
                Status = r.Status,
                SentAt = r.SentAt,
                ViewedAt = r.ViewedAt,
                SignedAt = r.SignedAt,
                DeclinedAt = r.DeclinedAt,
                DeclineReason = r.DeclineReason,
            }).ToList(),
        });
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<EnvelopeListDto>>> GetPagedAsync(
        EnvelopeQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        // 整个方法共用同一个 now：分页谓词、状态派生若各取各的时间，
        // 边界上那一份请求会被筛进来又被标成别的状态。
        var now = DateTime.UtcNow;
        var q = _requests.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();
            q = q.Where(r => r.Title.ToLower().Contains(keyword));
        }
        if (query.Status.HasValue)
            q = q.Where(EnvelopeExpiry.StatusFilter(query.Status.Value, now));
        if (!string.IsNullOrWhiteSpace(query.HostEntityType))
            q = q.Where(r => r.HostEntityType == query.HostEntityType);
        if (query.HostEntityId.HasValue)
            q = q.Where(r => r.HostEntityId == query.HostEntityId.Value);
        if (query.TemplateId.HasValue)
            q = q.Where(r => r.TemplateId == query.TemplateId.Value);

        var paged = await q
            .OrderByDescending(r => r.CreationTime)
            .ProjectTo<Envelope, EnvelopeListDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        foreach (var item in paged.Items)
            item.Status = EnvelopeExpiry.Derive(item.Status, item.ExpiresAt, now);

        // 进度（已签 / 总数）是列表页唯一想知道的收件人信息。
        // 单次分组查询回填，不做 N+1。
        var ids = paged.Items.Select(i => i.Id).ToList();
        if (ids.Count > 0)
        {
            var progress = await _recipients.AsNoTracking()
                .Where(s => ids.Contains(s.RequestId))
                .GroupBy(s => s.RequestId)
                .Select(g => new
                {
                    RequestId = g.Key,
                    Total = g.Count(),
                    Signed = g.Count(s => s.Status == SigningRecipientStatus.Signed),
                })
                .ToListAsync(cancellationToken);

            var map = progress.ToDictionary(p => p.RequestId);
            foreach (var item in paged.Items)
            {
                if (!map.TryGetValue(item.Id, out var p)) continue;
                item.RecipientCount = p.Total;
                item.SignedCount = p.Signed;
            }
        }

        return Ok(paged);
    }

    // ── 内部 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 全部签完则密封归档；顺序签署则唤醒下一位。
    /// </summary>
    private async Task AdvanceAsync(Envelope request, SigningSnapshot snapshot, CancellationToken cancellationToken)
    {
        var recipients = await LoadRecipientsAsync(request.Id, cancellationToken);

        if (recipients.Any(r => r.Status != SigningRecipientStatus.Signed))
        {
            if (request.IsSequential)
            {
                // 唤醒下一位排队者。
                var next = recipients.FirstOrDefault(r => r.Status == SigningRecipientStatus.Pending);
                if (next != null)
                {
                    next.Status = SigningRecipientStatus.Sent;
                    next.SentAt = DateTime.UtcNow;
                    await _recipients.UpdateAsync(next, cancellationToken: cancellationToken);
                }
            }

            if (request.Status == EnvelopeStatus.Sent)
                request.Status = EnvelopeStatus.InProgress;

            await _requests.UpdateAsync(request, cancellationToken: cancellationToken);
            await FlushAsync(cancellationToken);
            return;
        }

        // 全签完 → 密封。
        await SealAndArchiveAsync(request, snapshot, recipients, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<EnvelopeDto>> SealAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _requests.GetAsync(requestId, cancellationToken);
        if (request == null)
            return Fail<EnvelopeDto>("Signing request not found.", 404);

        if (request.Status == EnvelopeStatus.Completed || request.FinalPdfFileId is not null)
        {
            // 哈希只算一次：已密封的请求再密封一遍，就是让「这份 PDF 就是当初签的那份」失去意义。
            return Fail<EnvelopeDto>("This request has already been sealed.", 409);
        }
        if (request.Status != EnvelopeStatus.InProgress)
            return Fail<EnvelopeDto>("Only a request whose recipients have all signed can be sealed.", 409);

        var recipients = await LoadRecipientsAsync(requestId, cancellationToken);
        if (recipients.Count == 0 || recipients.Any(r => r.Status != SigningRecipientStatus.Signed))
            return Fail<EnvelopeDto>("Not every recipient has signed yet.", 409);

        var snapshot = SigningSnapshot.FromJson(request.TemplateSnapshotJson);
        if (snapshot == null)
            return Fail<EnvelopeDto>("This request's template snapshot cannot be read.", 409);

        // 密封要读渲染稿。调用方是管理端（控制器门是 signing.request.update），而请求名下的文件
        // 本就按 signing.request.view 放行（SigningFileReferenceAccessResolver）；这里的授予让服务层
        // 不依赖那个解析器有没有登记，且与最后一位收件人那次提交里的密封走同一条路。
        GrantDocumentAccess(request);

        if (!await SealAndArchiveAsync(request, snapshot, recipients, cancellationToken))
            return Fail<EnvelopeDto>("The document could not be sealed. See the server log for the cause.", 500);

        return await GetAsync(requestId, cancellationToken);
    }

    /// <summary>
    /// 密封的后半段：抢占 → 盖章压平算哈希存成品 → 完成证书 → 交宿主归档。返回是否密封成功。
    /// </summary>
    /// <remarks>
    /// 两个调用方：最后一位收件人的那次提交（<see cref="AdvanceAsync"/>），以及管理端的重新密封
    /// （<see cref="SealAsync"/>）—— 后者存在的理由是前者可能因为一次瞬时故障（存储超时、盖章异常）失败，
    /// 而那时收件人已全部 Signed、不能重交，没有这条路就只剩作废重发。
    /// </remarks>
    private async Task<bool> SealAndArchiveAsync(
        Envelope request,
        SigningSnapshot snapshot,
        IReadOnlyList<Signer> recipients,
        CancellationToken cancellationToken)
    {
        // ★ 先抢占密封权。并行签署时最后两位可能同时走到这里：收件人查询走 AsNoTracking，
        //   两边读到的都是刚落库的真值"全签完"，于是各密封一次 —— 两份成品、两个哈希，
        //   而先交给宿主归档的那一份不是最后记在请求上的那一份。抢占放在密封**之前**，
        //   输的一方连成品都不会生成，因此不留孤儿文件、也不会把自己那份塞给宿主。
        if (!await TryClaimSealAsync(request, cancellationToken))
            return request.Status == EnvelopeStatus.Completed;

        var values = await LoadValuesAsync(request.Id, cancellationToken);
        var sealResult = await _sealer.SealAsync(request, snapshot, values, recipients, cancellationToken);
        if (!sealResult.Succeeded || sealResult.Data is null)
        {
            // 密封失败：保持在 InProgress，不推进到 Completed。一份没有成品、
            // 没有哈希的"已完成"请求是谎报。抢占时写下的完成时刻一并退回去。
            LogError("Sealing signing request {RequestId} failed: {Message}", request.Id, sealResult.Message ?? "unknown");
            request.CompletedAt = null;
            request.Status = EnvelopeStatus.InProgress;
            await _requests.UpdateAsync(request, cancellationToken: cancellationToken);
            await FlushAsync(cancellationToken);
            return false;
        }

        request.FinalPdfFileId = sealResult.Data.FileId;
        request.Sha256 = sealResult.Data.Sha256;
        // CompletedAt 在抢占那一刻就写下了 —— 那才是最后一个签名到齐的时刻，
        // 而不是"盖完章存完文件之后"。
        request.Status = EnvelopeStatus.Completed;

        // 成品是在这个（匿名的）请求里刚存下的，CreatorId 为空：同一请求内接下来要读它的
        // 任何一方（宿主归档 sink、响应里的文档）都得靠授予。
        GrantDocumentAccess(request);

        // 完成证书在密封之后生成 —— 它要写进成品的哈希，所以顺序不能反。
        // ★ 生成失败**不回退**这次密封：文档已经签成、哈希已经算定，为一页审计记录
        //   把一份有效的签署结果撤回去是本末倒置。留 CompletionCertificateFileId 为空
        //   并记日志，之后可以补生成（证书是对既有事实的记述，随时重算都是同一份）。
        var certificate = await _certificates.BuildAsync(
            request, recipients, sealResult.Data.FileName, cancellationToken);
        if (certificate.Succeeded)
        {
            request.CompletionCertificateFileId = certificate.Data;
            _grants.Grant(certificate.Data);
        }
        else
        {
            LogError(
                "The completion certificate for signing request {RequestId} could not be produced: {Message}",
                request.Id, certificate.Message ?? "unknown");
        }

        await _requests.UpdateAsync(request, cancellationToken: cancellationToken);
        await FlushAsync(cancellationToken);

        // 交回宿主模块归档。sink 未注册不是错误（独立文档本就没有归档去处）。
        var sink = _registry.FindSink(request.HostEntityType);
        if (sink != null && request.HostEntityId is { } hostId)
        {
            try
            {
                await sink.AttachAsync(hostId, sealResult.Data.FileId, sealResult.Data.FileName, request.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                // ★ 归档失败不回滚密封：文档已经签成，哈希已经算定，把它撤回去
                //   等于毁掉一份有效的签署结果。留日志让人补挂，sink 本身要求幂等，
                //   所以重试是安全的。
                Logger.LogError(ex, "Attaching sealed document for request {RequestId} to its host failed.", request.Id);
            }
        }

        return true;
    }

    /// <summary>
    /// 用乐观并发戳（<see cref="IConcurrencyStamp"/>）抢占这份请求的密封权；抢到返回 true。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 抢占写在 <see cref="Envelope.CompletedAt"/> 上：那一刻最后一个签名确实已经到齐，
    /// 这个时间是真的。"完成了没有"的权威判据始终是 <see cref="Envelope.Status"/> ——
    /// 密封若失败，调用处会把完成时刻与状态一起退回去。
    /// </para>
    /// <para>
    /// ★ 抢占失败必须把实体从变更跟踪里<b>丢掉</b>：它仍然停在 Modified，会被本作用域
    /// 下一次 <c>SaveChanges</c> 重放，而那一次的异常会出现在完全无关的位置
    /// （见 <see cref="IRepository{TEntity}.Discard"/> 的说明）。
    /// </para>
    /// </remarks>
    private async Task<bool> TryClaimSealAsync(Envelope request, CancellationToken cancellationToken)
    {
        request.CompletedAt = DateTime.UtcNow;
        try
        {
            await _requests.UpdateAsync(request, cancellationToken);
            await FlushAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _requests.Discard(request);
            Logger.LogInformation(
                "Signing request {RequestId} is already being sealed by a concurrent submission; this one stops before sealing.",
                request.Id);

            // 这次提交的响应不该说"还在进行中"：把另一方刚落库的结果读回来。
            var current = await _requests.GetAsync(request.Id, cancellationToken);
            if (current != null)
            {
                request.Status = current.Status;
                request.CompletedAt = current.CompletedAt;
                request.FinalPdfFileId = current.FinalPdfFileId;
                request.Sha256 = current.Sha256;
                request.CompletionCertificateFileId = current.CompletionCertificateFileId;
            }

            return false;
        }
    }

    private async Task<List<Signer>> LoadRecipientsAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var list = await _recipients.ToListAsync(r => r.RequestId == requestId, cancellationToken);
        return list.OrderBy(r => r.Order).ToList();
    }

    private async Task<Dictionary<string, string?>> LoadValuesAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var rows = await _values.ToListAsync(v => v.RequestId == requestId, cancellationToken);
        return rows.ToDictionary(v => v.FieldKey, v => v.Value, StringComparer.Ordinal);
    }

    /// <summary>写入取值（同键覆盖）。</summary>
    private async Task StoreValuesAsync(
        Guid requestId,
        IReadOnlyDictionary<string, string?> values,
        Guid? recipientId,
        CancellationToken cancellationToken)
    {
        if (values.Count == 0) return;

        var existing = await _values.ToListAsync(v => v.RequestId == requestId, cancellationToken);
        var byKey = existing.ToDictionary(v => v.FieldKey, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (byKey.TryGetValue(key, out var row))
            {
                row.Value = value;
                row.RecipientId = recipientId ?? row.RecipientId;
                await _values.UpdateAsync(row, cancellationToken: cancellationToken);
            }
            else
            {
                await _values.InsertAsync(new FieldValue
                {
                    RequestId = requestId,
                    FieldKey = key,
                    RecipientId = recipientId,
                    Value = value,
                }, cancellationToken: cancellationToken);
            }
        }
    }
}
