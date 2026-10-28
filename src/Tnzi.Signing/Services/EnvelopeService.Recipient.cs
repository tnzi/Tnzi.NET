namespace Tnzi.Signing.Services;

/// <summary>
/// <see cref="EnvelopeService"/> 的<b>收件人面</b>：凭一次性令牌取件、取文档字节、提交、拒签。
/// </summary>
/// <remarks>
/// <para>
/// 与同名文件里的管理面（发起 / 发出 / 作废 / 查询 / 密封推进）刻意分成两个文件：这一半的调用者是
/// <b>匿名</b>的，身份完全由令牌担保，每一个入口都从 <see cref="ResolveTokenAsync"/> 开始，
/// 再各自过一道状态门（读：<see cref="IsReadable"/>；写：<see cref="CheckSignable"/>），
/// 过了门才把这份请求的文档写进请求级读取授予（<c>IFileAccessGrantContext</c>）。
/// 把它们放在一起，是为了让「哪些代码在匿名路径上」一眼可数。
/// </para>
/// <para>
/// ★ 读取面也看状态。作废 / 拒签 / 过期之后，链接不再交出字段值与文档（载荷只剩状态，好让消费方的
/// 签署页能说「这份请求已取消」）；作废还会**吊销令牌**（<c>VoidAsync</c> 清空 <c>TokenHash</c>），
/// 一份发错人的请求要收得回来。已完成的请求是刻意的例外：签署人凭自己的链接取回密封成品。
/// </para>
/// <para>
/// ★ 多租户：收件人没有租户上下文（没有登录、没有 JWT），而 <c>Signer</c> / <c>Envelope</c> / <c>FileRecord</c>
/// 都是 <c>IMultiTenant</c>，全局过滤器严格等值 —— 按常规查询，令牌在租户开启的部署上一个都解析不出，
/// 症状是「This signing link is not valid」。令牌是 256 位随机数，等值命中就钉死了那一行，比租户上下文更强；
/// 所以 <see cref="FindSignerByTokenAsync"/> 跨租户找 <c>Signer</c>，每个入口随即用 <see cref="EnterTenantOf"/>
/// 把这次请求切进那一行的租户，其余的读与密封的写都落在那里。**切换必须在公开方法自己的帧里做**：
/// <c>ICurrentTenant.Change</c> 写的是 AsyncLocal，在被 await 的方法里设置的值不会回流到调用方。
/// </para>
/// </remarks>
public partial class EnvelopeService
{
    /// <inheritdoc />
    public async Task<Result<SigningPacketDto>> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var signer = await FindSignerByTokenAsync(token, cancellationToken);
        if (signer == null)
            return Fail<SigningPacketDto>("This signing link is not valid.", 404);

        using var tenant = EnterTenantOf(signer);
        var (request, recipient, snapshot) = await ResolveTokenAsync(signer, cancellationToken);
        if (request == null || recipient == null || snapshot == null)
            return Fail<SigningPacketDto>("This signing link is not valid.", 404);

        var now = DateTime.UtcNow;
        var allSigned = EnvelopeExpiry.AllSigned(await LoadRecipientsAsync(request.Id, cancellationToken));
        if (!IsReadable(request, allSigned, now))
        {
            // 只剩状态：没有字段值、没有文档、不记查看时间（那个时间会进完成证书，而这份请求不会完成）。
            return Ok(StatusOnlyPacket(request, recipient, allSigned, now));
        }

        GrantDocumentAccess(request);

        // 首次打开记一次查看时间，这条时间会进完成证书。
        // 只写这两列，且只在它仍是首次打开时写（缘由见 WakeNextAsync：整行写回会把并发作废清掉的令牌写回去）。
        if (recipient.ViewedAt == null && recipient.Status == SigningRecipientStatus.Sent)
        {
            var viewedAt = DateTime.UtcNow;
            await _recipients.EnsureTransactionStartedAsync(cancellationToken);
            var stamped = await _recipients.AsQueryable()
                .Where(r => r.Id == recipient.Id && r.Status == SigningRecipientStatus.Sent && r.ViewedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.ViewedAt, viewedAt)
                    .SetProperty(r => r.Status, SigningRecipientStatus.Viewed), cancellationToken);
            if (stamped > 0)
            {
                recipient.ViewedAt = viewedAt;
                recipient.Status = SigningRecipientStatus.Viewed;
            }
        }

        return Ok(await BuildPacketAsync(request, recipient, snapshot, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<SigningDocumentContent>> GetDocumentByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var signer = await FindSignerByTokenAsync(token, cancellationToken);
        if (signer == null)
            return Fail<SigningDocumentContent>("This signing link is not valid.", 404);

        using var tenant = EnterTenantOf(signer);
        var (request, recipient, snapshot) = await ResolveTokenAsync(signer, cancellationToken);
        if (request == null || recipient == null || snapshot == null)
            return Fail<SigningDocumentContent>("This signing link is not valid.", 404);

        // 作废 / 拒签 / 过期的请求不再交出文档；与「没有文档」同一句话，收件人不需要知道区别。
        var allSigned = EnvelopeExpiry.AllSigned(await LoadRecipientsAsync(request.Id, cancellationToken));
        if (!IsReadable(request, allSigned, DateTime.UtcNow))
            return Fail<SigningDocumentContent>("This request has no document.", 404);

        // 完成后给密封成品，否则给渲染稿 —— 与 SigningPacketDto.DocumentFileId 同一口径。
        if ((request.FinalPdfFileId ?? request.RenderedPdfFileId) is not { } fileId)
            return Fail<SigningDocumentContent>("This request has no document.", 404);

        // 状态门过了，把这份请求的文档写进请求级授予，下面两次读取因此放行。
        GrantDocumentAccess(request);
        var record = await _files.GetRecordAsync(fileId);
        if (!record.Succeeded || record.Data is null)
            return Fail<SigningDocumentContent>("The document could not be read.", 404);

        // ★ 这个端点的契约是一份 PDF。渲染稿的 id 来自模板管理员的请求体，而 Storage 记下的
        // Content-Type 是按上传者给的文件名算出来的（.html → text/html）：控制器把它内联交给
        // 浏览器，一个 .html 就是跑在 API 源上的脚本。建模板那一侧已经拦了一次，这里是纵深 ——
        // 控制器可以被消费方整体替换，服务层是唯一必经处；存量模板也不经建模板那道门。
        // 与「读不到」同一句话：收件人不需要知道文档为什么不可用，管理端才需要。
        if (!PdfContentType.IsPdf(record.Data.ContentType))
            return Fail<SigningDocumentContent>("The document could not be read.", 404);

        var content = await _files.GetAsync(fileId);
        if (!content.Succeeded || content.Data is null)
            return Fail<SigningDocumentContent>("The document could not be read.", 404);

        return Ok(new SigningDocumentContent(
            content.Data,
            PdfContentType.MediaType,
            record.Data.OriginalName ?? record.Data.FileName));
    }

    /// <inheritdoc />
    public async Task<Result<SigningPacketDto>> SubmitAsync(string token, SubmitSigningDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var signer = await FindSignerByTokenAsync(token, cancellationToken);
        if (signer == null)
            return Fail<SigningPacketDto>("This signing link is not valid.", 404);

        using var tenant = EnterTenantOf(signer);
        var (request, recipient, snapshot) = await ResolveTokenAsync(signer, cancellationToken);
        if (request == null || recipient == null || snapshot == null)
            return Fail<SigningPacketDto>("This signing link is not valid.", 404);

        var gate = CheckSignable(request, recipient);
        if (gate != null) return Fail<SigningPacketDto>(gate.Message!, gate.Code ?? 409);

        // 密封在这次提交里读渲染稿、存成品：状态门过了才授予。
        GrantDocumentAccess(request);

        // ★ 两个匿名可写的自由文本字段先过形态与上限，早于任何写入：
        //   越界的图写不进 HasMaxLength 的列（数据库异常，不是一句可读的 400）；
        //   解不出来的图会等到密封盖章那一步才失败，而那时收件人已是 Signed、不能重交，
        //   整份请求就此钉死在 InProgress，没有任何人能修。
        var payloadCheck = ValidateSubmittedPayload(input);
        if (payloadCheck != null) return Fail<SigningPacketDto>(payloadCheck.Message!, payloadCheck.Code ?? 400);

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
            if (!submitted.TryGetValue(field.Key, out var v))
                continue;

            // 字段值是收件人填的、又会被画进成品的一个框里：越界按字段指名拒绝（列上限见 SigningLimits）。
            if (v is { Length: > SigningLimits.MaxFieldValueLength })
            {
                return Fail<SigningPacketDto>(
                    $"The value of '{field.Label}' is too long (maximum {SigningLimits.MaxFieldValueLength} characters).", 400);
            }

            // 勾选框只认 true / false（规范化后入库）：认不出的值若照收，密封会把它画成一个空框。
            if (!SigningFieldValues.TryNormalize(field, v, out var normalized))
                return Fail<SigningPacketDto>(SigningFieldValues.InvalidCheckboxMessage(field), 400);

            accepted[field.Key] = normalized;
        }

        // 必填的勾选框必须是勾上的：'false' 也是非空白，却会被密封成一个空的必勾框。
        var missing = mine
            .Where(f => f.Required && !SigningFieldValues.IsFilled(f, accepted.GetValueOrDefault(f.Key)))
            .Select(f => f.Label)
            .ToList();
        if (missing.Count > 0)
            return Fail<SigningPacketDto>($"These required fields are missing: {string.Join(", ", missing)}.", 400);

        // 该角色有签名字段却没交图 —— 拦下来，否则会密封出一份签名位空白的文档。
        var needsSignature = snapshot.Fields.Any(
            f => f.IsSignatureLike && string.Equals(f.RecipientRole, recipient.Role, StringComparison.OrdinalIgnoreCase));
        if (needsSignature && string.IsNullOrWhiteSpace(input.SignatureImage))
            return Fail<SigningPacketDto>("A signature is required.", 400);

        // 签署状态是一次认领（见 ClaimableRecipient），只写签署的这几列（整行写回会把并发作废清掉的令牌写回去）。
        // 认领不到就一个字都不写：令牌已被并发作废吊销、同一链接的另一次提交先签了、别人拒签让请求离开了进行中。
        // 认领与字段值在同一个工作单元里：字段值写失败时认领随之回滚，收件人仍可重交（重交按键覆盖取值）；
        // 先认领再写值，是为了输掉认领的那次提交不会把先签那次的取值覆盖掉（密封盖进 PDF 的是先签那次的）。
        var signatureImage = input.SignatureImage;
        var consentText = input.ConsentText;
        var signerIp = ScopedContext?.ClientIpAddress;
        var signerUserAgent = SigningLimits.Fit(ScopedContext?.UserAgent, SigningLimits.MaxSignerUserAgentLength);
        var signedAt = DateTime.UtcNow;
        var claimed = await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _recipients.EnsureTransactionStartedAsync(ct);
            var won = await ClaimableRecipient(recipient).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.SignatureImage, signatureImage)
                .SetProperty(r => r.ConsentText, consentText)
                .SetProperty(r => r.SignerIp, signerIp)
                .SetProperty(r => r.SignerUserAgent, signerUserAgent)
                .SetProperty(r => r.SignedAt, signedAt)
                .SetProperty(r => r.Status, SigningRecipientStatus.Signed), ct);
            if (won == 0)
                return false;

            await StoreValuesAsync(request.Id, accepted, recipient.Id, ct);
            return true;
        }, cancellationToken);

        if (!claimed)
        {
            var refusal = await RecipientClaimRefusalAsync(request.Id, recipient, cancellationToken);
            return Fail<SigningPacketDto>(refusal.Message!, refusal.Code ?? 409);
        }

        // ★ 签名一落库，推进（唤醒下一位 / 密封）就是服务端欠下的事，不再跟着这次 HTTP 请求的取消令牌走：
        // 收件人点完「签署」就关掉页面，或者网关先断开，取消会在推进的半途抛出：签名已是 Signed、不能重交，
        // 请求却停在半路。推进里的每一步都有自己的失败处理（密封失败退回 InProgress、可由管理端重新密封）。
        await AdvanceAsync(request, snapshot, CancellationToken.None);

        var refreshed = await LoadRecipientsAsync(request.Id, cancellationToken);
        var me = refreshed.First(r => r.Id == recipient.Id);
        return Ok(await BuildPacketAsync(request, me, snapshot, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<SigningPacketDto>> DeclineAsync(string token, string? reason, CancellationToken cancellationToken = default)
    {
        var signer = await FindSignerByTokenAsync(token, cancellationToken);
        if (signer == null)
            return Fail<SigningPacketDto>("This signing link is not valid.", 404);

        using var tenant = EnterTenantOf(signer);
        var (request, recipient, snapshot) = await ResolveTokenAsync(signer, cancellationToken);
        if (request == null || recipient == null || snapshot == null)
            return Fail<SigningPacketDto>("This signing link is not valid.", 404);

        var gate = CheckSignable(request, recipient);
        if (gate != null) return Fail<SigningPacketDto>(gate.Message!, gate.Code ?? 409);

        if (reason is { Length: > SigningLimits.MaxDeclineReasonLength })
        {
            return Fail<SigningPacketDto>(
                $"The decline reason is too long (maximum {SigningLimits.MaxDeclineReasonLength} characters).", 400);
        }

        // 拒签同样是一次认领、只写拒签的这几列（见 SubmitAsync）：认领不到就不碰信封。
        // ★ 认领与「信封改成 Declined」是同一个事实的两半，在同一个工作单元里写，要么一起生效要么一起不生效。
        //   信封那一写带乐观并发戳：两写之间落下的一次作废（另一位收件人的拒签、并行签署的推进同理）让它撞上冲突。
        //   此前认领先自动提交了，冲突抛成 500，收件人停在 Declined 而信封已经作废，两边的记录从此对不上。
        var declinedAt = DateTime.UtcNow;
        var signerIp = ScopedContext?.ClientIpAddress;
        var signerUserAgent = SigningLimits.Fit(ScopedContext?.UserAgent, SigningLimits.MaxSignerUserAgentLength);
        bool claimed;
        try
        {
            claimed = await ExecuteInUnitOfWorkAsync(async ct =>
            {
                await _recipients.EnsureTransactionStartedAsync(ct);
                var won = await ClaimableRecipient(recipient).ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, SigningRecipientStatus.Declined)
                    .SetProperty(r => r.DeclinedAt, declinedAt)
                    .SetProperty(r => r.DeclineReason, reason)
                    .SetProperty(r => r.SignerIp, signerIp)
                    .SetProperty(r => r.SignerUserAgent, signerUserAgent), ct);
                if (won == 0)
                    return false;

                // 一人拒签即整份作废：一份缺了一方签名的合同没有中间状态可言。
                request.Status = EnvelopeStatus.Declined;
                await _requests.UpdateAsync(request, cancellationToken: ct);
                await FlushAsync(ct);
                return true;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 工作单元已回滚：拒签一个字都没落库。写失败的信封实例仍停在 Modified，丢掉它，
            // 否则会被本作用域下一次 SaveChanges 重放；然后按库里此刻的状态答复。
            _requests.Discard(request);
            Logger.LogInformation(
                "Signing request {RequestId} changed while recipient {RecipientId} was declining it; the decline was not recorded.",
                request.Id, recipient.Id);
            var refusal = await RecipientClaimRefusalAsync(request.Id, recipient, cancellationToken, DeclineConflictRefusal);
            return Fail<SigningPacketDto>(refusal.Message!, refusal.Code ?? 409);
        }

        if (!claimed)
        {
            var refusal = await RecipientClaimRefusalAsync(request.Id, recipient, cancellationToken);
            return Fail<SigningPacketDto>(refusal.Message!, refusal.Code ?? 409);
        }

        recipient.Status = SigningRecipientStatus.Declined;
        recipient.DeclinedAt = declinedAt;
        recipient.DeclineReason = reason;
        recipient.SignerIp = signerIp;
        recipient.SignerUserAgent = signerUserAgent;

        return Ok(await BuildPacketAsync(request, recipient, snapshot, cancellationToken));
    }

    /// <summary>
    /// 令牌 → 收件人行，<b>跨租户</b>查找。
    /// </summary>
    /// <remarks>
    /// 收件人是匿名的，请求里没有租户；多租户开启时常规查询会退化成 <c>TenantId IS NULL</c>，一个都找不到。
    /// 令牌本身就钉死了那一行（256 位随机数，唯一索引），跨租户只用在这一次等值查询上，
    /// 而且只找 <c>Signer</c>：之后每一次读写都在 <see cref="EnterTenantOf"/> 切进的租户里，不放宽任何过滤器。
    /// 刻意不用 <c>IDataFilterManager.Disable</c>：它每次调用记一条跨租户 Warning，而这里每次打开链接都要查一次，
    /// 会把日志刷成噪音。软删条件手工补回（<c>IgnoreQueryFilters</c> 是整体摘除）。
    /// </remarks>
    private async Task<Signer?> FindSignerByTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        // 比对哈希，不拿秘密做等值查询。
        var hash = OneTimeToken.Hash(token);
        return await _recipients.AsQueryable()
            .IgnoreQueryFilters()
            .Where(r => !r.IsDeleted && r.TokenHash == hash)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 把这次请求切进收件人行所在的租户；已经在那个租户里（或多租户未开启）时什么都不做。
    /// 调用方 <c>using</c> 住返回值，且必须在自己的帧里调用（AsyncLocal 不回流）。
    /// </summary>
    /// <remarks>
    /// 已知限制：<c>signer.TenantId</c> 为 null（信封在无租户上下文里发起）而这个浏览器会话带着租户登录时，
    /// <c>Change(null)</c> 的覆盖会被 EF 过滤器的 <c>CurrentTenant?.Id ?? CurrentUser?.TenantId</c> 当成「没有覆盖」，
    /// 后续读取落回登录用户的租户而 404。那条 <c>??</c> 同时是「租户用户不能自己挑租户」的守卫，不由本模块单方面改；
    /// 匿名会话（这条路径的常态）不受影响。见 docs/modules/signing.md 多租户一节。
    /// </remarks>
    private IDisposable? EnterTenantOf(Signer signer)
    {
        if (_currentTenant.Id == signer.TenantId)
            return null;

        // Debug 而不是 Warning：这是匿名链接的常态（没有租户上下文），不是越权。
        // 记下来是为了让「租户缺席」与「令牌无效」在日志里分得开。
        Logger.LogDebug(
            "Signing link resolved into tenant {TenantId} (ambient tenant: {AmbientTenantId}).",
            signer.TenantId, _currentTenant.Id);
        return _currentTenant.Change(signer.TenantId);
    }

    /// <summary>收件人行 → (请求, 收件人, 快照)。任何一环不成立都返回全 null。调用方已切进该行的租户。</summary>
    private async Task<(Envelope?, Signer?, SigningSnapshot?)> ResolveTokenAsync(
        Signer recipient, CancellationToken cancellationToken)
    {
        var request = await _requests.GetAsync(recipient.RequestId, cancellationToken);
        if (request == null) return (null, null, null);

        var snapshot = SigningSnapshot.FromJson(request.TemplateSnapshotJson);
        // 快照解析不出 = 这份请求无法处理，绝不当作"没有字段"继续走。
        if (snapshot == null) return (null, null, null);

        return (request, recipient, snapshot);
    }

    /// <summary>
    /// 把这份请求名下的文档写进本次请求的读取授予。调用方先过状态门（读：<see cref="IsReadable"/>，
    /// 写：<see cref="CheckSignable"/>），不在令牌解析处一律授予。
    /// </summary>
    /// <remarks>
    /// ★ 令牌校验通过 = 这个人是这份文档的当事人。把文档 id 写进本次请求的读取授予，
    ///   Tnzi.Storage 的判定据此放行（判据 4，与分享链接同一形状）。少了这一步：
    ///   ① 密封在 SigningSealer 里 GetAsync(渲染稿) 必 404 —— 收件人是匿名的，而这条
    ///      控制器正是本模块唯一出货的签署入口，于是最后一位提交后信封永远停在 InProgress；
    ///   ② 收件人拿到 DocumentFileId 却取不到字节，看不见自己正在签的文档。
    ///   授予只给读、只在这一次请求内、只覆盖这份请求自己的文档 —— 不放宽任何守卫。
    /// </remarks>
    private void GrantDocumentAccess(Envelope request)
    {
        if (request.RenderedPdfFileId is { } rendered) _grants.Grant(rendered);
        if (request.FinalPdfFileId is { } final) _grants.Grant(final);
    }

    /// <summary>签名图与同意条款的形态与上限（见 <see cref="SigningLimits"/>）。</summary>
    private static Result? ValidateSubmittedPayload(SubmitSigningDto input)
    {
        if (input.SignatureImage is { Length: > SigningLimits.MaxSignatureImageLength })
            return Result.Failure(
                $"The signature image is too large (maximum {SigningLimits.MaxSignatureImageLength} characters).", 400);

        if (!string.IsNullOrWhiteSpace(input.SignatureImage))
        {
            switch (SignatureImagePayload.Inspect(input.SignatureImage))
            {
                case SignatureImageVerdict.TooLarge:
                    return Result.Failure(
                        $"The signature image is too large (at most {SigningLimits.MaxSignatureImageSide} pixels on a side "
                        + $"and {SigningLimits.MaxSignatureImagePixels} pixels in total).", 400);
                case SignatureImageVerdict.Malformed:
                    return Result.Failure(
                        "The signature image must be a complete PNG or JPEG, as a base64 data URL or a base64 string.", 400);
                case SignatureImageVerdict.Unsupported:
                    return Result.Failure(
                        "The signature image is a PNG or JPEG variant that cannot be stamped. "
                        + "Send an 8-bit PNG, or a baseline or progressive grayscale or RGB JPEG.", 400);
            }
        }

        if (input.ConsentText is { Length: > SigningLimits.MaxConsentTextLength })
            return Result.Failure(
                $"The consent text is too long (maximum {SigningLimits.MaxConsentTextLength} characters).", 400);

        return null;
    }

    /// <summary>
    /// 链接还能不能读（字段值 + 文档）：进行中且未过期，或已完成（签署人取回密封成品）。
    /// 作废 / 拒签 / 过期都不能 —— 一条本该失效的链接不该永远保有读权限。
    /// 全部签完、只差密封的请求不会过期（见 <see cref="EnvelopeExpiry"/>），签署人仍取得到自己签过的文档。
    /// </summary>
    private static bool IsReadable(Envelope request, bool allSigned, DateTime now)
    {
        var status = EnvelopeExpiry.Derive(request.Status, request.ExpiresAt, allSigned, now);
        return status == EnvelopeStatus.Completed || EnvelopeExpiry.IsPending(status);
    }

    /// <summary>失效链接的载荷：只有状态，好让消费方的签署页能说「这份请求已取消 / 已过期」。</summary>
    private static SigningPacketDto StatusOnlyPacket(Envelope request, Signer recipient, bool allSigned, DateTime now) => new()
    {
        Title = request.Title,
        RecipientName = recipient.Name,
        RecipientStatus = recipient.Status,
        RequestStatus = EnvelopeExpiry.Derive(request.Status, request.ExpiresAt, allSigned, now),
        IsMyTurn = false,
        Fields = [],
        DocumentFileId = null,
        ExpiresAt = request.ExpiresAt,
    };

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

    /// <summary>
    /// 签署 / 拒签那一次写入的认领条件：收件人仍持有读到的那枚令牌、还没签也没拒、所属请求仍在进行中（Sent / InProgress）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 状态门（<see cref="CheckSignable"/>）读的是一份快照，过门之后到写入之前这一行可能已经变了：管理端作废了请求
    /// （令牌被清空）、同一链接的另一次提交先签了（双击、超时重试）、另一位收件人拒签让请求成了 Declined。
    /// 只按 Id 写的话，签名落在一份已作废或已拒签的请求上；两次提交里后到的那次还会改掉先到那次的签名图、
    /// 同意文本与签署时间，而密封盖进成品的是先到那次的，记录与成品从此对不上。条件写在库里判，同一时刻只有一次占得到。
    /// </para>
    /// <para>令牌按读到的那一枚比，而不只是比非空：作废把它清成 null，任何轮换也让旧链接的写入落空。</para>
    /// </remarks>
    private IQueryable<Signer> ClaimableRecipient(Signer recipient)
    {
        var recipientId = recipient.Id;
        var tokenHash = recipient.TokenHash;
        var envelopes = _requests.AsQueryable();
        return _recipients.AsQueryable().Where(r => r.Id == recipientId
            && r.TokenHash != null && r.TokenHash == tokenHash
            && r.Status != SigningRecipientStatus.Signed
            && r.Status != SigningRecipientStatus.Declined
            && envelopes.Any(e => e.Id == r.RequestId
                && (e.Status == EnvelopeStatus.Sent || e.Status == EnvelopeStatus.InProgress)));
    }

    /// <summary>
    /// 拒签撞上信封并发冲突、而库里那份请求仍可签署时的答复：冲突来自一次无关的写入（并行签署的推进），
    /// 什么都没落库，重试会成功。
    /// </summary>
    private static readonly Result DeclineConflictRefusal =
        Result.Failure("The request changed while the decline was being recorded. Please try again.", 409);

    /// <summary>
    /// 认领没占到时的答复，按库里此刻的状态说：链接已被吊销（作废）答 404，与作废之后再打开链接同一句；
    /// 其余与状态门（<see cref="CheckSignable"/>）同一组答复。状态门放行时答 <paramref name="stillSignable"/>
    /// （缺省是「请求已不在进行中」）。
    /// </summary>
    private async Task<Result> RecipientClaimRefusalAsync(
        Guid requestId, Signer recipient, CancellationToken cancellationToken, Result? stillSignable = null)
    {
        var current = await _recipients.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recipient.Id, cancellationToken);
        if (current?.TokenHash == null || current.TokenHash != recipient.TokenHash)
            return Result.Failure("This signing link is not valid.", 404);

        var request = await _requests.AsNoTracking().FirstOrDefaultAsync(e => e.Id == requestId, cancellationToken);
        if (request == null)
            return Result.Failure("This signing link is not valid.", 404);

        return CheckSignable(request, current) ?? stillSignable ?? Result.Failure("This request is no longer active.", 409);
    }

    private static bool IsMyTurn(Envelope request, Signer recipient, IReadOnlyList<Signer> all)
    {
        if (!request.IsSequential) return true;
        var next = all.FirstOrDefault(r => r.Status != SigningRecipientStatus.Signed);
        return next == null || next.Id == recipient.Id;
    }

    private static RecipientFieldDto ToRecipientField(SnapshotField field, IReadOnlyDictionary<string, string?> values) => new()
    {
        Key = field.Key,
        Label = field.Label,
        Type = field.Type,
        Required = field.Required,
        Value = values.GetValueOrDefault(field.Key),
    };

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
            .Select(f => ToRecipientField(f, values))
            .ToList();

        // 发起方的字段（角色为空）只读交出：签署人要看得见将被密封的内容。
        var prefilled = snapshot.Fields
            .Where(f => !f.IsSignatureLike && string.IsNullOrWhiteSpace(f.RecipientRole))
            .Where(f => !string.IsNullOrEmpty(values.GetValueOrDefault(f.Key)))
            .Select(f => ToRecipientField(f, values))
            .ToList();

        return new SigningPacketDto
        {
            Title = request.Title,
            RecipientName = recipient.Name,
            RecipientStatus = recipient.Status,
            // 收件人看到的状态也要现算 —— 否则一个过期链接会显示"等待您签署"，
            // 而点下去必然被 CheckSignable 拒掉。
            RequestStatus = EnvelopeExpiry.Derive(request.Status, request.ExpiresAt, EnvelopeExpiry.AllSigned(all), DateTime.UtcNow),
            IsMyTurn = IsMyTurn(request, recipient, all),
            Fields = mine,
            PrefilledFields = prefilled,
            // 完成后给密封成品，否则给渲染稿。
            DocumentFileId = request.FinalPdfFileId ?? request.RenderedPdfFileId,
            ExpiresAt = request.ExpiresAt,
        };
    }
}
