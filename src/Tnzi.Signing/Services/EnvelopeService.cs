namespace Tnzi.Signing.Services;

/// <inheritdoc cref="IEnvelopeService" />
public class EnvelopeService : ApplicationService, IEnvelopeService
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
        IFileStorageService files)
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
    }

    /// <inheritdoc />
    public async Task<Result<EnvelopeDto>> CreateAsync(CreateEnvelopeDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (input.Recipients is not { Count: > 0 })
            return Fail<EnvelopeDto>("At least one recipient is required.", 400);

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
                Role = r.Role,
                Name = r.Name,
                Email = r.Email,
                Order = order++,
                // 令牌在 SendAsync 才签发：草稿阶段不该存在可用的签署链接。
                // null 而非空串——空串会撞上 TokenHash 的唯一索引（见实体上的说明）。
                TokenHash = null,
                Status = SigningRecipientStatus.Pending,
            }, cancellationToken: cancellationToken);
        }

        await StoreValuesAsync(request.Id, await ResolveInitialValuesAsync(request, snapshot, input, cancellationToken), null, cancellationToken);
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

    private static string SafeName(string title)
    {
        var safe = string.IsNullOrWhiteSpace(title) ? "document" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return safe.Length > 120 ? safe[..120] : safe;
    }

    /// <summary>合并变量 + 发起方预填，合成初始取值。</summary>
    private async Task<Dictionary<string, string?>> ResolveInitialValuesAsync(
        Envelope request,
        SigningSnapshot snapshot,
        CreateEnvelopeDto input,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        // 宿主记录的合并变量。provider 未注册或宿主为空都不是错误 ——
        // 一份独立文档本来就没有可合并的记录。
        var provider = _registry.FindProvider(request.HostEntityType);
        if (provider != null && request.HostEntityId is { } hostId)
        {
            var resolved = await provider.ResolveAsync(hostId, cancellationToken);
            foreach (var field in snapshot.Fields)
            {
                if (field.Binding is not { Length: > 0 } binding) continue;
                // ★ provider 省略某个键 = "这份记录没有这个信息"，与"值是空串"不同。
                //   这里也照此处理：不写进去，好让必填校验能在发出前拦下它。
                if (resolved.TryGetValue(binding, out var v) && v != null)
                    values[field.Key] = Convert.ToString(v, CultureInfo.InvariantCulture);
            }
        }

        // 发起方预填覆盖合并结果（人是权威的）。
        foreach (var (key, value) in input.PrefilledValues ?? [])
            values[key] = value;

        return values;
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

        var issued = new List<IssuedSigningLink>(recipients.Count);
        foreach (var recipient in recipients)
        {
            var token = SigningToken.Create();
            recipient.TokenHash = SigningToken.Hash(token);
            // 顺序签署时只有第一位处于"已送达"，其余仍在排队。
            recipient.Status = !request.IsSequential || recipient.Order == recipients[0].Order
                ? SigningRecipientStatus.Sent
                : SigningRecipientStatus.Pending;
            if (recipient.Status == SigningRecipientStatus.Sent)
                recipient.SentAt = DateTime.UtcNow;

            await _recipients.UpdateAsync(recipient, cancellationToken: cancellationToken);
            // ★ 明文只在这一刻存在于内存里；库里从此只有哈希。
            issued.Add(new IssuedSigningLink(recipient.Id, recipient.Name, recipient.Email, token));
        }

        request.Status = EnvelopeStatus.Sent;
        await _requests.UpdateAsync(request, cancellationToken: cancellationToken);
        await FlushAsync(cancellationToken);

        return Ok<IReadOnlyList<IssuedSigningLink>>(issued);
    }

    /// <inheritdoc />
    public async Task<Result<SigningPacketDto>> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var (request, recipient, snapshot) = await ResolveTokenAsync(token, cancellationToken);
        if (request == null || recipient == null || snapshot == null)
            return Fail<SigningPacketDto>("This signing link is not valid.", 404);

        // 首次打开记一次查看时间，这条时间会进完成证书。
        if (recipient.ViewedAt == null && recipient.Status == SigningRecipientStatus.Sent)
        {
            recipient.ViewedAt = DateTime.UtcNow;
            recipient.Status = SigningRecipientStatus.Viewed;
            await _recipients.UpdateAsync(recipient, cancellationToken: cancellationToken);
            await FlushAsync(cancellationToken);
        }

        return Ok(await BuildPacketAsync(request, recipient, snapshot, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<SigningPacketDto>> SubmitAsync(string token, SubmitSigningDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var (request, recipient, snapshot) = await ResolveTokenAsync(token, cancellationToken);
        if (request == null || recipient == null || snapshot == null)
            return Fail<SigningPacketDto>("This signing link is not valid.", 404);

        var gate = CheckSignable(request, recipient);
        if (gate != null) return Fail<SigningPacketDto>(gate.Message!, gate.Code ?? 409);

        var recipients = await LoadRecipientsAsync(request.Id, cancellationToken);
        if (request.IsSequential && !IsMyTurn(request, recipient, recipients))
            return Fail<SigningPacketDto>("It is not this recipient's turn to sign yet.", 409);

        // 只接受本角色负责的字段：一个收件人不该能改另一个人要签的内容。
        var mine = snapshot.Fields
            .Where(f => !f.IsSignatureLike
                        && string.Equals(f.RecipientRole, recipient.Role, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var submitted = input.Values ?? [];
        var accepted = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var field in mine)
        {
            if (submitted.TryGetValue(field.Key, out var v))
                accepted[field.Key] = v;
        }

        var missing = mine
            .Where(f => f.Required && string.IsNullOrWhiteSpace(accepted.GetValueOrDefault(f.Key)))
            .Select(f => f.Label)
            .ToList();
        if (missing.Count > 0)
            return Fail<SigningPacketDto>($"These required fields are missing: {string.Join(", ", missing)}.", 400);

        // 该角色有签名字段却没交图 —— 拦下来，否则会密封出一份签名位空白的文档。
        var needsSignature = snapshot.Fields.Any(
            f => f.IsSignatureLike && string.Equals(f.RecipientRole, recipient.Role, StringComparison.OrdinalIgnoreCase));
        if (needsSignature && string.IsNullOrWhiteSpace(input.SignatureImage))
            return Fail<SigningPacketDto>("A signature is required.", 400);

        await StoreValuesAsync(request.Id, accepted, recipient.Id, cancellationToken);

        recipient.SignatureImage = input.SignatureImage;
        recipient.ConsentText = input.ConsentText;
        recipient.SignerIp = ScopedContext?.ClientIpAddress;
        recipient.SignerUserAgent = ScopedContext?.UserAgent;
        recipient.SignedAt = DateTime.UtcNow;
        recipient.Status = SigningRecipientStatus.Signed;
        await _recipients.UpdateAsync(recipient, cancellationToken: cancellationToken);
        await FlushAsync(cancellationToken);

        await AdvanceAsync(request, snapshot, cancellationToken);

        var refreshed = await LoadRecipientsAsync(request.Id, cancellationToken);
        var me = refreshed.First(r => r.Id == recipient.Id);
        return Ok(await BuildPacketAsync(request, me, snapshot, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<SigningPacketDto>> DeclineAsync(string token, string? reason, CancellationToken cancellationToken = default)
    {
        var (request, recipient, snapshot) = await ResolveTokenAsync(token, cancellationToken);
        if (request == null || recipient == null || snapshot == null)
            return Fail<SigningPacketDto>("This signing link is not valid.", 404);

        var gate = CheckSignable(request, recipient);
        if (gate != null) return Fail<SigningPacketDto>(gate.Message!, gate.Code ?? 409);

        recipient.Status = SigningRecipientStatus.Declined;
        recipient.DeclinedAt = DateTime.UtcNow;
        recipient.DeclineReason = reason;
        recipient.SignerIp = ScopedContext?.ClientIpAddress;
        recipient.SignerUserAgent = ScopedContext?.UserAgent;
        await _recipients.UpdateAsync(recipient, cancellationToken: cancellationToken);

        // 一人拒签即整份作废：一份缺了一方签名的合同没有中间状态可言。
        request.Status = EnvelopeStatus.Declined;
        await _requests.UpdateAsync(request, cancellationToken: cancellationToken);
        await FlushAsync(cancellationToken);

        return Ok(await BuildPacketAsync(request, recipient, snapshot, cancellationToken));
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
        // ★ 先抢占密封权。并行签署时最后两位可能同时走到这里：收件人查询走 AsNoTracking，
        //   两边读到的都是刚落库的真值"全签完"，于是各密封一次 —— 两份成品、两个哈希，
        //   而先交给宿主归档的那一份不是最后记在请求上的那一份。抢占放在密封**之前**，
        //   输的一方连成品都不会生成，因此不留孤儿文件、也不会把自己那份塞给宿主。
        if (!await TryClaimSealAsync(request, cancellationToken))
            return;

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
            return;
        }

        request.FinalPdfFileId = sealResult.Data.FileId;
        request.Sha256 = sealResult.Data.Sha256;
        // CompletedAt 在抢占那一刻就写下了 —— 那才是最后一个签名到齐的时刻，
        // 而不是"盖完章存完文件之后"。
        request.Status = EnvelopeStatus.Completed;

        // 完成证书在密封之后生成 —— 它要写进成品的哈希，所以顺序不能反。
        // ★ 生成失败**不回退**这次密封：文档已经签成、哈希已经算定，为一页审计记录
        //   把一份有效的签署结果撤回去是本末倒置。留 CompletionCertificateFileId 为空
        //   并记日志，之后可以补生成（证书是对既有事实的记述，随时重算都是同一份）。
        var certificate = await _certificates.BuildAsync(
            request, recipients, sealResult.Data.FileName, cancellationToken);
        if (certificate.Succeeded)
        {
            request.CompletionCertificateFileId = certificate.Data;
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

    /// <summary>令牌 → (请求, 收件人, 快照)。任何一环不成立都返回全 null。</summary>
    private async Task<(Envelope?, Signer?, SigningSnapshot?)> ResolveTokenAsync(
        string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return (null, null, null);

        // 比对哈希，不拿秘密做等值查询。
        var hash = SigningToken.Hash(token);
        var recipient = await _recipients.FirstOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
        if (recipient == null) return (null, null, null);

        var request = await _requests.GetAsync(recipient.RequestId, cancellationToken);
        if (request == null) return (null, null, null);

        var snapshot = SigningSnapshot.FromJson(request.TemplateSnapshotJson);
        // 快照解析不出 = 这份请求无法处理，绝不当作"没有字段"继续走。
        return snapshot == null ? (null, null, null) : (request, recipient, snapshot);
    }

    /// <summary>还能不能签。</summary>
    private static Result? CheckSignable(Envelope request, Signer recipient)
    {
        if (request.Status is EnvelopeStatus.Voided or EnvelopeStatus.Declined)
            return Result.Failure("This request is no longer active.", 409);
        if (request.Status == EnvelopeStatus.Completed)
            return Result.Failure("This request has already been completed.", 409);
        if (request.ExpiresAt <= DateTime.UtcNow)
            return Result.Failure("This request has expired.", 410);
        if (recipient.Status == SigningRecipientStatus.Signed)
            return Result.Failure("This recipient has already signed.", 409);
        if (recipient.Status == SigningRecipientStatus.Declined)
            return Result.Failure("This recipient has already declined.", 409);
        return null;
    }

    private static bool IsMyTurn(Envelope request, Signer recipient, IReadOnlyList<Signer> all)
    {
        if (!request.IsSequential) return true;
        var next = all.FirstOrDefault(r => r.Status != SigningRecipientStatus.Signed);
        return next == null || next.Id == recipient.Id;
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

    private async Task<SigningPacketDto> BuildPacketAsync(
        Envelope request,
        Signer recipient,
        SigningSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var all = await LoadRecipientsAsync(request.Id, cancellationToken);
        var values = await LoadValuesAsync(request.Id, cancellationToken);

        var mine = snapshot.Fields
            .Where(f => !f.IsSignatureLike
                        && string.Equals(f.RecipientRole, recipient.Role, StringComparison.OrdinalIgnoreCase))
            .Select(f => new RecipientFieldDto
            {
                Key = f.Key,
                Label = f.Label,
                Type = f.Type,
                Required = f.Required,
                Value = values.GetValueOrDefault(f.Key),
            })
            .ToList();

        return new SigningPacketDto
        {
            Title = request.Title,
            RecipientName = recipient.Name,
            RecipientStatus = recipient.Status,
            // 收件人看到的状态也要现算 —— 否则一个过期链接会显示"等待您签署"，
            // 而点下去必然被 CheckSignable 拒掉。
            RequestStatus = EnvelopeExpiry.Derive(request.Status, request.ExpiresAt, DateTime.UtcNow),
            IsMyTurn = IsMyTurn(request, recipient, all),
            Fields = mine,
            // 完成后给密封成品，否则给渲染稿。
            DocumentFileId = request.FinalPdfFileId ?? request.RenderedPdfFileId,
            ExpiresAt = request.ExpiresAt,
        };
    }
}
