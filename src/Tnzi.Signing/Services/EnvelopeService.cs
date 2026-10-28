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

        var inputCheck = CheckCreateInput(input);
        if (inputCheck != null)
            return Fail<EnvelopeDto>(inputCheck.Message!, inputCheck.Code ?? 400);

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

        if (!HostEntityTypeList.Allows(template.HostEntityTypes, input.HostEntityType))
        {
            // 模板限定了宿主类型：它的绑定字段与 {{变量}} 是照那几类记录的合并变量写的。绑到别的类型上，
            // 变量去另一个 provider 那里解析、全都解析不出：必填的在发出时被拦下，不必填的留白密封。
            return Fail<EnvelopeDto>(
                $"This template can only be used for these host types: {string.Join(", ", HostEntityTypeList.Parse(template.HostEntityTypes))}.",
                400);
        }

        if (input.HostEntityId is not null
            && _registry.FindSink(input.HostEntityType) != null
            && _registry.FindProvider(input.HostEntityType) == null)
        {
            // ★ 绑定唯一的授权点是同类型 provider 的 ResolveAsync（见 IMergeSourceProvider 与 IDocumentHostSink 的契约）。
            //   只注册了 sink 的宿主类型，绑定不经任何授权，完成时 sink 照样把这份 PDF 挂进发起人点名的那条记录。
            //   契约要求两者成对注册；缺了 provider 是部署缺陷，这里失败关闭，而不是放行一次没人判过的绑定。
            Logger.LogError(
                "Host type {HostEntityType} has an IDocumentHostSink but no IMergeSourceProvider, so a binding to one of its records cannot be authorized; the request is refused. Register a provider for this host type.",
                input.HostEntityType);
            return Fail<EnvelopeDto>(
                $"Requests cannot be bound to a '{input.HostEntityType}' record: this host type has no merge source provider to authorize the binding.",
                500);
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
        // ★ 宿主记录只解析一次，绑定字段与 Composed 正文用同一份（键不分大小写）：此前各调一次 provider，
        //   正文的 {{clientName}} 按不分大小写取到了值，绑定 'clientName' 的字段却按 provider 字典自己的比较器
        //   对不上 'ClientName'，发出时被必填校验拦下或留白密封；两次调用之间记录变了，两处还会各取一份。
        var merge = await ResolveMergeValuesAsync(input, cancellationToken);
        var resolvedValues = ResolveInitialValues(snapshot, input, merge);
        var lengthCheck = CheckInitialValueLengths(snapshot, resolvedValues);
        if (lengthCheck != null)
            return Fail<EnvelopeDto>(lengthCheck.Message!, lengthCheck.Code ?? 400);

        var normalizedValues = NormalizeInitialValues(snapshot, resolvedValues);
        if (!normalizedValues.Succeeded)
            return Fail<EnvelopeDto>(normalizedValues.Message!, normalizedValues.Code ?? 400);
        var initialValues = normalizedValues.Data!;

        var title = string.IsNullOrWhiteSpace(input.Title) ? template.Name : input.Title.Trim();
        var renderedPdfFileId = template.RenderedPdfFileId;

        if (template.Source == TemplateSource.Composed)
        {
            // ★ Composed 模板**每份请求各排一次版**，不是模板存一份渲染稿：正文里带合并
            //   变量，不同宿主记录排出来的分页可能不同，字段落点也就不同。共用一份渲染稿
            //   等于让第二份文档的签名框停在第一份文档的位置上。
            var composed = await RenderComposedAsync(template, snapshot, merge, title, cancellationToken);
            if (!composed.Succeeded)
                return Fail<EnvelopeDto>(composed.Message ?? "The document could not be rendered.", composed.Code ?? 500);

            renderedPdfFileId = composed.Data.FileId;
            snapshot = composed.Data.Snapshot;
        }
        else if (snapshot.Fields.Any(f => f.PlacementMode == FieldPlacementMode.Anchor) && renderedPdfFileId is { } documentId)
        {
            // 上传型模板的锚在建模板时已经核对过；这里再对一次，是为了不经那道门的存量模板：
            // 一个找不到的锚在密封时只会被静默跳过，而那时每个人都已经签完了。
            // 读模板渲染稿要授予：调用方是管理端（控制器门 signing.request.create），这份文档随后就会挂在这份请求名下。
            _grants.Grant(documentId);
            var document = await _sealer.ReadDocumentAsync(documentId, cancellationToken);
            if (document is null)
                return Fail<EnvelopeDto>("The template's document could not be read.", 500);

            var anchors = CheckAnchors(document, snapshot.Fields);
            if (anchors != null)
                return Fail<EnvelopeDto>(anchors.Message!, anchors.Code ?? 400);
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
            // 草稿上的截止时间只是占位：有效期从发出那一刻起算，SendAsync 按同样的天数重新定（见那里）。
            ExpiresAt = DateTime.UtcNow.AddDays(Math.Max(1, input.ExpiresInDays)),
            SentByUserId = CurrentUser?.Id,
            SentByName = CurrentUser?.UserName,
        };

        // 请求、收件人、初始取值一起提交：此前请求行先单独落库，收件人写入失败时留下一份没有收件人的草稿
        // （列表里看得见、发不出去、也没有删除端点）。
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _requests.InsertAsync(request, cancellationToken: ct);
            await FlushAsync(ct);

            var order = 1;
            foreach (var r in input.Recipients)
            {
                await _recipients.InsertAsync(new Signer
                {
                    RequestId = request.Id,
                    Role = r.Role.Trim(),
                    Name = r.Name.Trim(),
                    Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim(),
                    Order = order++,
                    // 令牌在 SendAsync 才签发：草稿阶段不该存在可用的签署链接。
                    // null 而非空串：空串会撞上 TokenHash 的唯一索引（见实体上的说明）。
                    TokenHash = null,
                    Status = SigningRecipientStatus.Pending,
                }, cancellationToken: ct);
            }

            await StoreValuesAsync(request.Id, initialValues, null, ct);
            await FlushAsync(ct);
        }, cancellationToken);

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
        IReadOnlyDictionary<string, object?> merge,
        string title,
        CancellationToken cancellationToken)
    {
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

        // 正文里没出现的字段保持模板上的定位方式：其中锚定位的，锚文本必须在刚排出来的这份文档里找得到。
        var anchors = CheckAnchors(rendered.Pdf, updated.Fields);
        if (anchors != null)
            return Result<(Guid, SigningSnapshot)>.Failure(anchors.Message!, anchors.Code ?? 400);

        using var stream = new MemoryStream(rendered.Pdf, writable: false);
        var saved = await _files.SaveAsync($"{SafeName(title)}.pdf", stream);
        if (!saved.Succeeded || saved.Data is null)
            return Result<(Guid, SigningSnapshot)>.Failure("The rendered document could not be stored.", 500);

        return Result<(Guid, SigningSnapshot)>.Success((saved.Data.Id, updated));
    }

    /// <summary>
    /// 发起方写入的文本按列宽校验，有效期按上限校验；<see langword="null"/> = 都合格。
    /// </summary>
    /// <remarks>
    /// 早于任何落库与排版：此前只校验了字段值长度，标题、宿主类型、收件人姓名 / 角色 / 邮箱、预填键越界在
    /// SQL Server / PostgreSQL 上都是一次 <c>DbUpdateException</c>（500），一个很大的有效期让计算截止时间抛异常（500，
    /// Composed 模板还已经存下了一份渲染稿）。姓名为空也在这里拦：HTTP 入口有模型校验，直接调服务的程序调用方没有。
    /// </remarks>
    private static Result? CheckCreateInput(CreateEnvelopeDto input)
    {
        if (input.ExpiresInDays > SigningLimits.MaxExpiresInDays)
            return Result.Failure($"The validity period can be at most {SigningLimits.MaxExpiresInDays} days.", 400);

        var problems = new List<string>();

        void Limit(string? value, int max, string what)
        {
            if (value is { Length: > 0 } && value.Length > max)
                problems.Add($"{what} is longer than {max} characters");
        }

        Limit(input.Title?.Trim(), SigningLimits.MaxTitleLength, "The title");
        Limit(input.HostEntityType, SigningLimits.MaxHostEntityTypeLength, "The host entity type");

        foreach (var (recipient, index) in input.Recipients.Select((r, i) => (r, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(recipient.Name))
                problems.Add($"recipient {index} has no name");
            Limit(recipient.Name?.Trim(), SigningLimits.MaxSignerNameLength, $"The name of recipient {index}");
            Limit(recipient.Role?.Trim(), SigningLimits.MaxRoleLength, $"The role of recipient {index}");
            Limit(recipient.Email?.Trim(), SigningLimits.MaxEmailLength, $"The email of recipient {index}");
        }

        foreach (var key in (input.PrefilledValues ?? []).Keys)
            Limit(key, SigningLimits.MaxFieldKeyLength, $"The prefilled field key '{key.FitToLength(40)}'");

        return problems.Count == 0
            ? null
            : Result.Failure($"The request cannot be created: {string.Join("; ", problems)}.", 400);
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

    /// <summary>
    /// 锚定位字段的锚文本必须在这份请求的文档里找得到；<see langword="null"/> = 都找得到（或没有锚定位字段）。
    /// </summary>
    /// <remarks>
    /// 与密封时的定位是同一套匹配（<see cref="SigningSealer.FindUnplacedAnchors"/>）：这里放行的锚，密封时一定找得到。
    /// 找不到的锚在密封时只会让那个字段被静默跳过，签名框空着，成品照样算哈希、出证书、归档：
    /// 所以在任何人签字之前就拦下，并逐个指名（键 + 锚文本）好让人去改模板。
    /// </remarks>
    private Result? CheckAnchors(byte[] document, IReadOnlyList<SnapshotField> fields)
    {
        IReadOnlyList<SnapshotField> unplaced;
        try
        {
            unplaced = _sealer.FindUnplacedAnchors(document, fields);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Searching the document for anchor text failed.");
            return Result.Failure("The template's document is not a readable PDF.", 409);
        }

        if (unplaced.Count == 0)
            return null;

        var named = unplaced.Select(f => $"'{f.Key}' (anchor '{f.AnchorText?.Trim()}')");
        return Result.Failure(
            $"The anchor text of these fields was not found in the document: {string.Join(", ", named)}. "
            + "Correct the template before starting a request from it.",
            400);
    }

    private static string SafeName(string title) => SigningLimits.FileNameStem(title, 120);

    /// <summary>
    /// 解析宿主记录的合并变量，整份请求只调一次 provider；键不分大小写，与 Composed 正文的 <c>{{var}}</c> 同一口径。
    /// </summary>
    /// <remarks>
    /// provider 未注册或宿主为空都不是错误：一份独立文档本来就没有可合并的记录，返回空字典。
    /// 值为 <see langword="null"/> 的键原样保留，由使用方按「省略」处理。
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, object?>> ResolveMergeValuesAsync(
        CreateEnvelopeDto input,
        CancellationToken cancellationToken)
    {
        var merge = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var provider = _registry.FindProvider(input.HostEntityType);
        if (provider != null && input.HostEntityId is { } hostId)
        {
            foreach (var (key, value) in await provider.ResolveAsync(hostId, cancellationToken))
                merge[key] = value;
        }

        return merge;
    }

    /// <summary>合并变量 + 发起方预填，合成初始取值。</summary>
    private static Dictionary<string, string?> ResolveInitialValues(
        SigningSnapshot snapshot,
        CreateEnvelopeDto input,
        IReadOnlyDictionary<string, object?> merge)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var field in snapshot.Fields)
        {
            if (field.Binding is not { Length: > 0 } binding) continue;
            // ★ provider 省略某个键 = "这份记录没有这个信息"，与"值是空串"不同。
            //   这里也照此处理：不写进去，好让 SendAsync 的必填校验能在发出前拦下它。
            if (merge.TryGetValue(binding, out var v) && v != null)
                values[field.Key] = Convert.ToString(v, CultureInfo.InvariantCulture);
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

    /// <summary>
    /// 发起方那侧的取值按字段类型规范化：勾选框只认 <c>true</c> / <c>false</c>（与收件人提交同一契约，见 <see cref="SigningFieldValues"/>）。
    /// </summary>
    /// <remarks>
    /// 合并变量给的布尔值转成字符串是 <c>True</c> / <c>False</c>，照常认得。认不出的值（<c>yes</c>、<c>on</c>）按字段指名 400：
    /// 照收的话，它在发出时被烧进渲染稿、密封时被画成一个空框。预填的键可能不对应任何快照字段，那些原样保留。
    /// </remarks>
    private static Result<Dictionary<string, string?>> NormalizeInitialValues(
        SigningSnapshot snapshot,
        IReadOnlyDictionary<string, string?> values)
    {
        var fields = snapshot.Fields.ToDictionary(f => f.Key, StringComparer.Ordinal);
        var normalized = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (!fields.TryGetValue(key, out var field))
            {
                normalized[key] = value;
                continue;
            }

            if (!SigningFieldValues.TryNormalize(field, value, out var canonical))
                return Result<Dictionary<string, string?>>.Failure(SigningFieldValues.InvalidCheckboxMessage(field), 400);

            normalized[key] = canonical;
        }

        return Result<Dictionary<string, string?>>.Success(normalized);
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
            .Where(f => !SigningFieldValues.IsFilled(f, values.GetValueOrDefault(f.Key)))
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

        request.ExpiresAt = ValidFromNow(request, DateTime.UtcNow);
        request.Status = EnvelopeStatus.Sent;
        await _requests.UpdateAsync(request, cancellationToken: cancellationToken);
        await FlushAsync(cancellationToken);

        return Ok<IReadOnlyList<IssuedSigningLink>>(issued);
    }

    /// <summary>
    /// 有效期从发出那一刻起算：按草稿上记下的天数（截止时间 − 创建时间）重新定截止时间。
    /// </summary>
    /// <remarks>
    /// 截止时间在建草稿时就定了，而草稿常常要等人审过才发：一份有效期 3 天的草稿一周后才发，此前发出的链接
    /// 一打开就是已过期（只给状态、提交 410），列表里刚发出就显示过期；隔几天才发的，收件人实际拿到的天数也比
    /// 「有效期（天）」少。天数取整：创建时间在落库那一刻才写，比算截止时间晚几毫秒。拿不到创建时间时原样不动。
    /// </remarks>
    private static DateTime ValidFromNow(Envelope request, DateTime now)
    {
        if (request.CreationTime == default || request.ExpiresAt <= request.CreationTime)
            return request.ExpiresAt;

        var days = Math.Max(1, Math.Round((request.ExpiresAt - request.CreationTime).TotalDays));
        return now.AddDays(days);
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
        // ★ 一条条件写、只动这一列：读出收件人再整行写回的话，读写之间落下的一次签署（Status / 签名图 / 签署时间）
        //   会被这份旧快照改回去。
        await _recipients.EnsureTransactionStartedAsync(cancellationToken);
        await _recipients.AsQueryable()
            .Where(r => r.RequestId == requestId && r.TokenHash != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.TokenHash, (string?)null), cancellationToken);

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
            Status = EnvelopeExpiry.Derive(request.Status, request.ExpiresAt, EnvelopeExpiry.AllSigned(recipients), DateTime.UtcNow),
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

        // 进度（已签 / 总数）是列表页唯一想知道的收件人信息，也是状态派生要的那一项（全部签完的不会过期）。
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

        foreach (var item in paged.Items)
        {
            item.Status = EnvelopeExpiry.Derive(
                item.Status, item.ExpiresAt, EnvelopeExpiry.AllSigned(item.RecipientCount, item.SignedCount), now);
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
                    await WakeNextAsync(next, cancellationToken);
            }

            // ★ 信封只在状态真的变了（Sent → InProgress，第一个签名到达）时才写。每一次写都会推进乐观并发戳，
            //   而最后一位签署人的密封抢占比的正是这个戳：一次什么都没改的写，会让一个手上拿着旧戳、
            //   此刻正要去抢占的并行签署人撞上冲突（抢占那侧现在会重读再抢，但没有理由制造这种冲突）。
            if (request.Status == EnvelopeStatus.Sent)
            {
                request.Status = EnvelopeStatus.InProgress;
                try
                {
                    await _requests.UpdateAsync(request, cancellationToken: cancellationToken);
                    await FlushAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // 这一行在我们读出它之后被别人写过：并行签署里另一位签署人已经抢占了密封（甚至已经密封完），
                    // 或者管理端刚刚作废了它。这一签早在上面就已落库，推进这一步本身已经没有意义：
                    // 读回库里的现状如实回报，而不是把一次已经成功的签署报成 500。
                    await AdoptCurrentRowAsync(request, cancellationToken);
                    Logger.LogInformation(
                        "Signing request {RequestId} changed while this submission was advancing it (now {Status}); the signature is recorded and the current state is reported.",
                        request.Id, request.Status);
                }

                return;
            }

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

        if (request.Status == EnvelopeStatus.Completed && request.FinalPdfFileId is not null)
        {
            // ★ 已密封：哈希只算一次，成品绝不重做，再盖一遍就是让「这份 PDF 就是当初签的那份」失去意义。
            //   这里只补做密封**之后**的两步：完成证书（缺失时）与交宿主归档。两者在密封那次提交里失败都只记日志、
            //   不回退密封，而契约（IDocumentHostSink 必须幂等、证书可补生成）承诺的那次「重试」就是这一步。
            return await CompleteSealedAsync(request, cancellationToken);
        }
        if (request.FinalPdfFileId is not null)
            return Fail<EnvelopeDto>("This request has already been sealed.", 409);
        // Sent 也收：单收件人的请求在最后一次提交里直接走向密封，从不经过 InProgress，
        // 而那次密封若在抢占之后抛异常（旧版本没有兜住），留下的就是 Sent + 全员 Signed。
        // 「全员已签」由下面逐一核对，状态这里只排除草稿与终态。
        if (request.Status is not (EnvelopeStatus.InProgress or EnvelopeStatus.Sent))
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

        // ★ 抢占一旦落库（CompletedAt 已写下），把密封做完或者干净地退回就是服务端的事：
        // 此后一律不用调用方的取消令牌。客户端断开不该让请求停在「有完成时刻、没有成品」的半路上。
        var ct = CancellationToken.None;

        SealResult sealedDocument;
        try
        {
            var values = await LoadValuesAsync(request.Id, ct);
            var sealResult = await _sealer.SealAsync(request, snapshot, values, recipients, ct);
            if (!sealResult.Succeeded || sealResult.Data is null)
            {
                LogError("Sealing signing request {RequestId} failed: {Message}", request.Id, sealResult.Message ?? "unknown");
                await ReleaseSealClaimAsync(request);
                return false;
            }

            sealedDocument = sealResult.Data;
        }
        catch (Exception ex)
        {
            // ★ 抛出来的异常与返回失败的 Result 一样处理：存储读写超时、定位锚点失败都是抛的。
            // 此前只有后者退回 InProgress，前者让单收件人的请求停在 Sent + CompletedAt：
            // 收件人重交答「已签」、管理端重新密封答「只有全员已签的才能密封」，只剩作废重签一条路。
            Logger.LogError(ex, "Sealing signing request {RequestId} threw; it is left InProgress and can be sealed again.", request.Id);
            await ReleaseSealClaimAsync(request);
            return false;
        }

        request.FinalPdfFileId = sealedDocument.FileId;
        request.Sha256 = sealedDocument.Sha256;
        // CompletedAt 在抢占那一刻就写下了 —— 那才是最后一个签名到齐的时刻，
        // 而不是"盖完章存完文件之后"。
        request.Status = EnvelopeStatus.Completed;

        // ★ 成品与哈希先落库，再做证书与归档：之后哪一步失败都不会让一份已经存下的成品成为孤儿，
        // 也不会出现「成品在、请求却说没密封」。
        await _requests.UpdateAsync(request, cancellationToken: ct);
        await FlushAsync(ct);

        // 成品是在这个（匿名的）请求里刚存下的，CreatorId 为空：同一请求内接下来要读它的
        // 任何一方（宿主归档 sink、响应里的文档）都得靠授予。
        GrantDocumentAccess(request);

        // 证书与归档失败都只记日志：密封已经成立，补做走管理端对这份已完成请求的再一次 seal（CompleteSealedAsync）。
        await RunPostSealStepsAsync(request, recipients, sealedDocument.FileName, ct);
        return true;
    }

    /// <summary>
    /// 已密封的请求：补做密封之后的两步（完成证书缺失时生成、交宿主归档），不碰成品与哈希。
    /// </summary>
    /// <remarks>
    /// 任何一步仍然失败就答 500 并说出是哪一步，这是管理员主动发起的恢复，他需要知道补没补上；
    /// 请求本身保持已完成，成品与哈希原样。
    /// </remarks>
    private async Task<Result<EnvelopeDto>> CompleteSealedAsync(Envelope request, CancellationToken cancellationToken)
    {
        var recipients = await LoadRecipientsAsync(request.Id, cancellationToken);

        // 宿主 sink 可能要读成品；与密封那条路同一理由授予。
        GrantDocumentAccess(request);

        // 一旦开始就做完，不跟随调用方的取消令牌（与密封同一理由：不留半路状态）。
        var failed = await RunPostSealStepsAsync(
            request, recipients, SigningSealer.SealedFileName(request.Title), CancellationToken.None);
        if (failed.Count > 0)
        {
            return Fail<EnvelopeDto>(
                $"The request is sealed, but {string.Join(" and ", failed)}. See the server log for the cause.", 500);
        }

        return await GetAsync(request.Id, cancellationToken);
    }

    /// <summary>
    /// 密封之后的两步：完成证书（尚未生成时）→ 交宿主归档。返回没做成的步骤（空 = 都做成了）；失败只记日志、不抛。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两个调用方：密封那一次（<see cref="SealAndArchiveAsync"/>，在最后一位签署人的匿名请求里），
    /// 以及管理端对一份已完成请求的再一次 seal（<see cref="CompleteSealedAsync"/>）。后者就是契约里那次「重试」：
    /// <see cref="IDocumentHostSink"/> 因此必须幂等，证书因此可以补生成。
    /// </para>
    /// <para>
    /// ★ 完成证书在密封之后生成，它要写进成品的哈希，所以顺序不能反。生成失败<b>不回退</b>密封：
    /// 文档已经签成、哈希已经算定，为一页审计记录把一份有效的签署结果撤回去是本末倒置。
    /// 补生成得到的是同一份内容（证书是对既有事实的记述：哈希、完成时刻、每位签署人的记录都已定死）。
    /// </para>
    /// <para>
    /// ★ 归档失败同样不回滚密封。sink 未注册不是错误（独立文档本就没有归档去处）。
    /// 已经生成过证书的请求不再生成第二份：一份请求只该有一张完成证书。
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> RunPostSealStepsAsync(
        Envelope request,
        IReadOnlyList<Signer> recipients,
        string sealedFileName,
        CancellationToken ct)
    {
        var failed = new List<string>();

        if (request.CompletionCertificateFileId is null)
        {
            try
            {
                var certificate = await _certificates.BuildAsync(request, recipients, sealedFileName, ct);
                if (certificate.Succeeded)
                {
                    request.CompletionCertificateFileId = certificate.Data;
                    _grants.Grant(certificate.Data);
                    await _requests.UpdateAsync(request, cancellationToken: ct);
                    await FlushAsync(ct);
                }
                else
                {
                    LogError(
                        "The completion certificate for signing request {RequestId} could not be produced: {Message}",
                        request.Id, certificate.Message ?? "unknown");
                    failed.Add("the completion certificate could not be produced");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "The completion certificate for signing request {RequestId} could not be produced.", request.Id);
                failed.Add("the completion certificate could not be produced");
            }
        }

        var sink = _registry.FindSink(request.HostEntityType);
        if (sink != null && request.HostEntityId is { } hostId && request.FinalPdfFileId is { } finalId)
        {
            try
            {
                await sink.AttachAsync(hostId, finalId, sealedFileName, request.Id, ct);
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Attaching the sealed document of signing request {RequestId} to its host failed; sealing the completed request again from the admin side retries it.",
                    request.Id);
                failed.Add("the sealed document could not be attached to its host record");
            }
        }

        return failed;
    }

    /// <summary>
    /// 密封没做成，就退回抢占前的样子：没有完成时刻，状态 InProgress（单收件人的请求也是：它所有人都签了），
    /// 管理端据此可以重新密封。
    /// </summary>
    /// <remarks>
    /// 一份没有成品、没有哈希的「已完成」请求是谎报，所以只退不进。不用调用方的取消令牌：退回本身被取消，
    /// 请求就停在「有完成时刻、没有成品」的半路上。
    /// </remarks>
    private async Task ReleaseSealClaimAsync(Envelope request)
    {
        request.CompletedAt = null;
        request.Status = EnvelopeStatus.InProgress;
        await _requests.UpdateAsync(request, cancellationToken: CancellationToken.None);
        await FlushAsync(CancellationToken.None);
    }

    /// <summary>抢占撞上的并发冲突来自无关写入时，按新戳重抢的次数上限（含第一次）。</summary>
    private const int MaxSealClaimAttempts = 3;

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
    /// ★ <b>并发冲突不等于「有人在密封」。</b>并发戳在信封的<b>每一次</b>写入时都会推进，所以冲突只说明
    /// 「这一行在我读出它之后被人写过」。并行签署里最常见的那个写入者是另一位签署人的非末位推进
    /// （Sent → InProgress）：B 先读出信封，A 签完、看到 B 还没签、推进信封，B 再签完、看到全签完、拿旧戳去抢：
    /// 此前这一下被当成「已经有人在密封」，结果是全员 Signed 而没有任何人密封。所以冲突之后读回那一行再判：
    /// 已有完成时刻 / 已不在 Sent·InProgress / 已有成品 = 真的输了；否则按新戳再抢（次数有上限）。
    /// 「全员已签」不必重核：签名只会从未签变成已签，作废与拒签都会让状态离开 Sent·InProgress。
    /// </para>
    /// <para>
    /// ★ 抢占失败必须把实体从变更跟踪里<b>丢掉</b>：它仍然停在 Modified，会被本作用域
    /// 下一次 <c>SaveChanges</c> 重放，而那一次的异常会出现在完全无关的位置
    /// （见 <see cref="IRepository{TEntity}.Discard"/> 的说明）。
    /// </para>
    /// </remarks>
    private async Task<bool> TryClaimSealAsync(Envelope request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
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
                // 这次提交的响应不该说"还在进行中"：把库里那一行（含另一方刚落库的结果）读回来。
                if (!await AdoptCurrentRowAsync(request, cancellationToken))
                    return false;

                if (request.CompletedAt is not null
                    || request.FinalPdfFileId is not null
                    || request.Status is not (EnvelopeStatus.Sent or EnvelopeStatus.InProgress))
                {
                    Logger.LogInformation(
                        "Signing request {RequestId} is already being sealed by a concurrent submission (or is no longer open); this one stops before sealing.",
                        request.Id);
                    return false;
                }

                if (attempt >= MaxSealClaimAttempts)
                {
                    // 一直被无关写入抢先：不在这里硬抢下去。收件人已全员 Signed，管理端可以重新密封。
                    Logger.LogWarning(
                        "Signing request {RequestId} could not be claimed for sealing after {Attempts} attempts because it kept changing underneath; it stays {Status} and can be sealed from the admin side.",
                        request.Id, attempt, request.Status);
                    return false;
                }

                Logger.LogDebug(
                    "Signing request {RequestId} changed before the seal claim (attempt {Attempt}); it is still open, claiming again with the current stamp.",
                    request.Id, attempt);
            }
        }
    }

    /// <summary>
    /// 把库里这一行的现状（含并发戳）读回调用方手上的这个实例，并让它脱离变更跟踪。找不到这一行时返回 false。
    /// </summary>
    /// <remarks>
    /// 写失败的实例仍停在 Modified，不丢掉会被本作用域下一次 <c>SaveChanges</c> 重放。读回走无跟踪查询，
    /// 好让下一次 <c>UpdateAsync</c> 直接附着这个实例（变更跟踪里没有同主键的另一份），并按刚读回的戳比对。
    /// 调用方（提交、重新密封）之后用的都是这个实例，所以是就地读回而不是换一个实例。
    /// </remarks>
    private async Task<bool> AdoptCurrentRowAsync(Envelope request, CancellationToken cancellationToken)
    {
        _requests.Discard(request);

        var current = await _requests.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken);
        if (current == null)
            return false;

        request.Status = current.Status;
        request.CompletedAt = current.CompletedAt;
        request.FinalPdfFileId = current.FinalPdfFileId;
        request.Sha256 = current.Sha256;
        request.CompletionCertificateFileId = current.CompletionCertificateFileId;
        request.RenderedPdfFileId = current.RenderedPdfFileId;
        request.TemplateSnapshotJson = current.TemplateSnapshotJson;
        request.ConcurrencyStamp = current.ConcurrencyStamp;
        return true;
    }

    /// <summary>
    /// 顺序签署时唤醒下一位：只在它此刻仍是 <c>Pending</c>、仍持有链接时写 <c>Status</c> / <c>SentAt</c> 两列。
    /// </summary>
    /// <remarks>
    /// ★ 不把读出来的快照整行写回：快照读出之后，管理端可能已经作废了这份请求（<c>TokenHash</c> 置空）。
    /// 整行写回会把旧的令牌哈希写回去，作废之后的链接重新解析得出，答的是一份只有状态的数据包而不是 404。
    /// 令牌已被吊销的人不再唤醒：他手上没有能用的链接。
    /// </remarks>
    private async Task WakeNextAsync(Signer next, CancellationToken cancellationToken)
    {
        var sentAt = DateTime.UtcNow;
        await _recipients.EnsureTransactionStartedAsync(cancellationToken);
        var woken = await _recipients.AsQueryable()
            .Where(r => r.Id == next.Id && r.Status == SigningRecipientStatus.Pending && r.TokenHash != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, SigningRecipientStatus.Sent)
                .SetProperty(r => r.SentAt, sentAt), cancellationToken);

        if (woken > 0)
        {
            next.Status = SigningRecipientStatus.Sent;
            next.SentAt = sentAt;
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
