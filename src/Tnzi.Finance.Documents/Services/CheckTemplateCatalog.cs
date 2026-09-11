using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Finance.Documents.Services;

/// <summary>
/// 支票版式目录的默认实现：出厂内置版式 + 模板库里自建的模板，合并成一份清单
/// </summary>
/// <remarks>
/// 合并规则（清单顺序 = 出厂版式在前、自建模板在后，各自内部按声明 / 名称排序）：
/// <list type="bullet">
/// <item><b>出厂版式</b>恒在清单里，即使模板库里还没有对应的行 —— 那只表示还没播种
///       （下次启动补上），不该让选择器少一项；此时 <c>IsSeeded=false</c>。</item>
/// <item>库里已有同名行 → 用行上的 <c>Description</c>/<c>IsActive</c>（管理端可能已改过它），
///       几何元数据仍取出厂声明（模板正文的坐标只有代码知道）。</item>
/// <item><b>自建模板</b>（库里有、出厂目录没有）→ <c>IsBuiltIn=false</c>，
///       几何元数据留空：框架不去猜一份自己没写过的模板一页印几张。</item>
/// </list>
/// </remarks>
public class CheckTemplateCatalog : ICheckTemplateCatalog
{
    private readonly IReadOnlyRepository<TemplateEntity, Guid>? _templateRepository;
    private readonly ILogger<CheckTemplateCatalog> _logger;

    public CheckTemplateCatalog(
        ILogger<CheckTemplateCatalog> logger,
        IReadOnlyRepository<TemplateEntity, Guid>? templateRepository = null)
    {
        _logger = Check.NotNull(logger);
        // 模板存储缺席（未接数据库上下文）时只出出厂清单：选择器少了自建模板，
        // 但绝不因此整个失败 —— 出厂版式本来就够选。
        _templateRepository = templateRepository;
    }

    /// <inheritdoc />
    public CheckTemplateResolution Resolve(string? requestedTemplateName, CheckLayout layout)
        => BuiltInCheckTemplates.Resolve(requestedTemplateName, layout);

    /// <inheritdoc />
    public async Task<Result<List<CheckTemplateDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var stored = await LoadStoredAsync(cancellationToken);

        var catalogue = new List<CheckTemplateDto>();

        foreach (var builtIn in BuiltInCheckTemplates.All)
        {
            stored.TryGetValue(builtIn.Name, out var row);
            catalogue.Add(new CheckTemplateDto
            {
                Name = builtIn.Name,
                DisplayName = builtIn.DisplayName,
                Description = string.IsNullOrWhiteSpace(row?.Description) ? builtIn.Description : row!.Description,
                Region = builtIn.Region,
                PaperSize = builtIn.PaperSize,
                ChecksPerPage = builtIn.ChecksPerPage,
                Position = builtIn.Position,
                SupportedStockTypes = builtIn.SupportedStockTypes.ToList(),
                IsBuiltIn = true,
                IsActive = row?.IsActive ?? true,
                IsSeeded = row != null
            });
        }

        foreach (var row in stored.Values.Where(r => BuiltInCheckTemplates.Find(r.Name) == null).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            catalogue.Add(new CheckTemplateDto
            {
                Name = row.Name,
                DisplayName = row.Name,
                Description = row.Description,
                ChecksPerPage = 1,
                IsBuiltIn = false,
                IsActive = row.IsActive,
                IsSeeded = true
            });
        }

        return Result<List<CheckTemplateDto>>.Success(catalogue);
    }

    /// <summary>模板库里 Module=Tnzi.Finance / Category=Check 的行（按名索引，投影到最小字段集）。</summary>
    private async Task<Dictionary<string, StoredTemplate>> LoadStoredAsync(CancellationToken cancellationToken)
    {
        if (_templateRepository == null)
            return new Dictionary<string, StoredTemplate>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var rows = await _templateRepository.AsNoTracking()
                .Where(t => t.Module == CheckTemplates.Module && t.Category == CheckTemplates.Category)
                .Select(t => new StoredTemplate(t.TemplateName, t.Description, t.IsActive))
                .ToListAsync(cancellationToken);

            // 同名行在 (Module, Category, TemplateName) 上有唯一索引，多租户下按租户分区，
            // 故这里不会撞键；仍用 TryAdd 以免一次数据异常把整个选择器打成 500。
            var byName = new Dictionary<string, StoredTemplate>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
                byName.TryAdd(row.Name, row);
            return byName;
        }
        catch (Exception ex)
        {
            // 目录是画选择器用的辅助读取，读不到时退回纯出厂清单比整页报错有用得多。
            _logger.LogWarning(ex, "Could not read the stored check templates; returning the built-in layouts only.");
            return new Dictionary<string, StoredTemplate>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed record StoredTemplate(string Name, string? Description, bool IsActive);
}
