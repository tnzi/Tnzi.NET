namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// <see cref="CheckService"/> 的版式一面：这一次用哪套版式、有哪些版式可选、
/// 某一套长什么样、以及对准它用的校准标尺。
/// </summary>
/// <remarks>
/// 与登记簿一面切开不只是为了文件长度：这里除模板解析外的每个动作都<b>不碰任何一张支票</b> ——
/// 不分配号、不写登记簿、不动账，甚至不需要有付款单或收款人存在。
/// 另一半（打印 / 预览 / 登记 / 作废 / 毁票 / 重打 / 重新渲染）恰好全都相反。
/// <para>
/// 仍是同一个类（<c>partial</c>）而不是另一个协作者：这几个成员与打印路径共用
/// 同一组仓储、同一个渲染器与同一份 501 文案，拆成独立服务只会把那些依赖各注入一遍。
/// </para>
/// </remarks>
public partial class CheckService
{
    /// <summary>
    /// 把请求上的单次模板覆盖叠加到银行档案设置上，并解析出<b>实际生效的模板名</b>。
    /// </summary>
    /// <remarks>
    /// 解析这一步（留空 → 按版式取出厂默认模板）必须在这里做一次，而不是留给渲染器：
    /// 写进支票快照的必须是已解析的名字，否则「快照为 null」既可能表示"当时就没指定"、
    /// 也可能表示"没有快照"，重新渲染一张历史支票时会退回**当前**档案的模板 ——
    /// 那正是钉快照要防的事。目录缺席时按原样传递（此时渲染器多半也缺席，端点已 501）。
    /// </remarks>
    private CheckPrintSettings ResolvePrintSettings(BankAccount bank, string? templateOverride)
    {
        var settings = CheckPrintSettings.FromBank(bank).WithTemplateOverride(templateOverride);
        var resolved = _templateCatalog?.Resolve(settings.TemplateName, settings.Layout).TemplateName;
        return settings.WithResolvedTemplate(resolved);
    }

    public async Task<Result<List<CheckTemplateDto>>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        if (_templateCatalog == null)
            return Fail<List<CheckTemplateDto>>(CatalogMissingMessage, 501);

        return await _templateCatalog.GetAllAsync(cancellationToken);
    }

    public async Task<Result<CheckFileDto>> GetTemplateSpecimenAsync(
        string templateName,
        CheckStockType stockType = CheckStockType.PrePrinted,
        Guid? bankAccountId = null,
        CancellationToken cancellationToken = default)
    {
        if (_renderer == null)
            return Fail<CheckFileDto>(RendererMissingMessage, 501);
        if (_templateCatalog == null)
            return Fail<CheckFileDto>(CatalogMissingMessage, 501);
        if (string.IsNullOrWhiteSpace(templateName))
            return Fail<CheckFileDto>("Specify which check layout to render a specimen of.", 400);

        // 先在目录里认一认这个名字：认不出就 404，而不是让渲染器抛一个
        // "模板不存在"的 500 —— 目录端点已经把可选值列出来了，答案应当一致。
        var catalogue = await _templateCatalog.GetAllAsync(cancellationToken);
        if (!catalogue.Succeeded)
            return Fail<CheckFileDto>(catalogue.Message ?? "The check layout catalogue is unavailable.", catalogue.Code ?? 500);

        var name = templateName.Trim();
        var entry = catalogue.Data!.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return Fail<CheckFileDto>($"Check layout '{name}' is not in the layout catalogue.", 404);

        // 可选绑定一个真实档案：借它的银行名 / 路由号 / 档案名让样张更有参考价值。
        // 账号绝不参与（见 BuildSpecimenRequest）。不绑定就用中性占位。
        BankAccount? bank = null;
        if (bankAccountId.HasValue)
        {
            bank = await _bankAccountRepository.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == bankAccountId.Value, cancellationToken);
            if (bank == null)
                return Fail<CheckFileDto>("Bank account not found.", 404);
        }

        // 每页几张由版式自己声明：每页三张的版式只画一张，用户就看不出它是三联的。
        var resolution = _templateCatalog.Resolve(entry.Name, LayoutFor(entry.ChecksPerPage));
        var settings = new CheckPrintSettings(
            entry.Name,
            LayoutFor(resolution.ChecksPerPage),
            stockType,
            // 偏移跟着档案走：绑定了档案的样张要和那台打印机真会印出来的位置一致
            bank?.OffsetXMm ?? 0m,
            bank?.OffsetYMm ?? 0m);

        var items = CheckSpecimenSample.BuildItems(resolution.ChecksPerPage, bank?.Currency ?? _options.BaseCurrency);
        // 未绑定档案时按版式所属区域选路由号方案：CPA-006 的样张画 transit/institution，美式的画 routing
        var scheme = bank?.Scheme
            ?? (string.Equals(entry.Region, "CA", StringComparison.OrdinalIgnoreCase) ? BankNumberScheme.CaEft : BankNumberScheme.UsAba);

        var request = _composer.BuildSpecimenRequest(bank, settings, items, scheme);

        var renderResult = await _renderer.RenderAsync(request, cancellationToken);
        if (!renderResult.Succeeded)
            return Fail<CheckFileDto>(renderResult.Message ?? "Check rendering failed.", renderResult.Code ?? 500);

        return Ok(CheckBatchComposer.BuildFile(_renderer, $"specimen_{entry.Name}", renderResult.Data!));
    }

    /// <summary>每页张数 → 版式枚举（模型上的 <c>Layout</c> 要与实际排版自洽）。</summary>
    private static CheckLayout LayoutFor(int checksPerPage)
        => checksPerPage >= 3 ? CheckLayout.ThreePerPage : CheckLayout.Voucher;

    public async Task<Result<CheckFileDto>> GetCalibrationPdfAsync(Guid bankAccountId, CancellationToken cancellationToken = default)
    {
        if (_renderer == null)
            return Fail<CheckFileDto>(RendererMissingMessage, 501);
        ICheckDocumentRenderer renderer = _renderer; // 上面已排除 null，捕获非空局部（await 后字段 null-state 会重置）

        var bank = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bankAccountId, cancellationToken);
        if (bank == null)
            return Fail<CheckFileDto>("Bank account not found.", 404);

        // 校准页要报出「你正在校准哪一套版式」，故同样走解析（否则模板名留空时它印的是
        // "默认模板"这个不存在的名字，而人拿着这张纸去和真票纸比对）。
        var settings = ResolvePrintSettings(bank, templateOverride: null);

        // ★ 与样张同口径：校准页会被下载、被打印，而白纸票纸下磁码行是真的印出来的。
        // 内置的两个校准渲染器只画标尺与磁码带轮廓，用不到账号 —— 但 ICheckDocumentRenderer
        // 是**可替换扩展点**，换一个会画磁码数字的实现，这张纸的磁性编码就与真票无异。
        // 所以既不解密（useStoredAccountNumber: false），也照样给出占位账号让磁码带画得出来。
        var request = _composer.BuildRenderRequest(bank, new List<CheckRenderItem>(), settings, useStoredAccountNumber: false);
        if (settings.StockType == CheckStockType.Blank)
            request.AccountNumberPlain = CheckSpecimenSample.AccountNumber;

        var renderResult = await renderer.RenderCalibrationAsync(request, cancellationToken);
        if (!renderResult.Succeeded)
            return Fail<CheckFileDto>(renderResult.Message!, renderResult.Code ?? 500);

        return Ok(CheckBatchComposer.BuildFile(renderer, $"calibration_{bank.Name}", renderResult.Data!));
    }
}
