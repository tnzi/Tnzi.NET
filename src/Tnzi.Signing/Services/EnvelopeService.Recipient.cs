namespace Tnzi.Signing.Services;

/// <summary>
/// <see cref="EnvelopeService"/> 的<b>收件人面</b>：凭一次性令牌取件、取文档字节、提交、拒签。
/// </summary>
/// <remarks>
/// <para>
/// 与同名文件里的管理面（发起 / 发出 / 作废 / 查询 / 密封推进）刻意分成两个文件：这一半的调用者是
/// <b>匿名</b>的，身份完全由令牌担保，每一个入口都从 <see cref="ResolveTokenAsync"/> 开始 ——
/// 那里也是把这份请求的文档写进请求级读取授予（<c>IFileAccessGrantContext</c>）的唯一位置。
/// 把它们放在一起，是为了让「哪些代码在匿名路径上」一眼可数。
/// </para>
/// </remarks>
public partial class EnvelopeService
{
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
    public async Task<Result<SigningDocumentContent>> GetDocumentByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var (request, recipient, snapshot) = await ResolveTokenAsync(token, cancellationToken);
        if (request == null || recipient == null || snapshot == null)
            return Fail<SigningDocumentContent>("This signing link is not valid.", 404);

        // 完成后给密封成品，否则给渲染稿 —— 与 SigningPacketDto.DocumentFileId 同一口径。
        if ((request.FinalPdfFileId ?? request.RenderedPdfFileId) is not { } fileId)
            return Fail<SigningDocumentContent>("This request has no document.", 404);

        // ResolveTokenAsync 已把这份请求的文档写进请求级授予，下面两次读取因此放行。
        var record = await _files.GetRecordAsync(fileId);
        if (!record.Succeeded || record.Data is null)
            return Fail<SigningDocumentContent>("The document could not be read.", 404);

        var content = await _files.GetAsync(fileId);
        if (!content.Succeeded || content.Data is null)
            return Fail<SigningDocumentContent>("The document could not be read.", 404);

        return Ok(new SigningDocumentContent(
            content.Data,
            record.Data.ContentType ?? "application/pdf",
            record.Data.OriginalName ?? record.Data.FileName));
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

    /// <summary>令牌 → (请求, 收件人, 快照)。任何一环不成立都返回全 null。</summary>
    private async Task<(Envelope?, Signer?, SigningSnapshot?)> ResolveTokenAsync(
        string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return (null, null, null);

        // 比对哈希，不拿秘密做等值查询。
        var hash = OneTimeToken.Hash(token);
        var recipient = await _recipients.FirstOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
        if (recipient == null) return (null, null, null);

        var request = await _requests.GetAsync(recipient.RequestId, cancellationToken);
        if (request == null) return (null, null, null);

        var snapshot = SigningSnapshot.FromJson(request.TemplateSnapshotJson);
        // 快照解析不出 = 这份请求无法处理，绝不当作"没有字段"继续走。
        if (snapshot == null) return (null, null, null);

        // ★ 令牌校验通过 = 这个人是这份文档的当事人。把文档 id 写进本次请求的读取授予，
        //   Tnzi.Storage 的判定据此放行（判据 4，与分享链接同一形状）。少了这一步：
        //   ① 密封在 SigningSealer 里 GetAsync(渲染稿) 必 404 —— 收件人是匿名的，而这条
        //      控制器正是本模块唯一出货的签署入口，于是最后一位提交后信封永远停在 InProgress；
        //   ② 收件人拿到 DocumentFileId 却取不到字节，看不见自己正在签的文档。
        //   授予只给读、只在这一次请求内、只覆盖这份请求自己的文档 —— 不放宽任何守卫。
        GrantDocumentAccess(request);
        return (request, recipient, snapshot);
    }

    /// <summary>把这份请求名下的文档写进本次请求的读取授予。</summary>
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

        if (!string.IsNullOrWhiteSpace(input.SignatureImage) && !SignatureImagePayload.IsWellFormed(input.SignatureImage))
            return Result.Failure("The signature image must be a base64 data URL or a base64 string.", 400);

        if (input.ConsentText is { Length: > SigningLimits.MaxConsentTextLength })
            return Result.Failure(
                $"The consent text is too long (maximum {SigningLimits.MaxConsentTextLength} characters).", 400);

        return null;
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
