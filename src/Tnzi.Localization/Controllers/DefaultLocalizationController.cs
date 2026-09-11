namespace Tnzi.Localization.Controllers;

/// <summary>
/// 本地化 API 控制器
/// 提供语言列表和资源查询接口
/// </summary>
[DefaultController]
[Route("localization")]
[ApiExplorerSettings(GroupName = "system")]
[AllowAnonymous]
public class DefaultLocalizationController : ApiControllerBase
{
    private readonly IOptions<RequestLocalizationOptions> _localizationOptions;
    private readonly IStringLocalizerFactory _localizerFactory;

    public DefaultLocalizationController(
        IOptions<RequestLocalizationOptions> localizationOptions,
        IStringLocalizerFactory localizerFactory)
    {
        _localizationOptions = Check.NotNull(localizationOptions);
        _localizerFactory = Check.NotNull(localizerFactory);
    }

    /// <summary>
    /// 获取支持的语言列表
    /// </summary>
    [HttpGet("cultures")]
    public virtual ApiResult<CultureListDto> GetCultures()
    {
        var options = _localizationOptions.Value;
        var defaultCulture = options.DefaultRequestCulture.Culture.Name;

        var cultures = options.SupportedCultures?
            .Select(c => new CultureDto
            {
                Name = c.Name,
                DisplayName = c.DisplayName,
                IsDefault = string.Equals(c.Name, defaultCulture, StringComparison.OrdinalIgnoreCase)
            })
            .ToList() ?? [];

        return Ok(new CultureListDto { Cultures = cultures });
    }

    /// <summary>
    /// 获取指定语言的所有资源。
    ///
    /// ★ 必须先按受支持语言名单校验 <paramref name="culture"/>。这个端点是
    /// <c>[AllowAnonymous]</c> 的，而 <see cref="CultureInfo"/> 会为几乎任意字符串
    /// 造出一个"自定义文化"而不抛异常 —— 直接交给它意味着每个没见过的名字都在
    /// 单例工厂的 <c>GetOrAdd</c> 里留下一条永不回收的缓存项，外加两次
    /// <c>File.Exists</c>：一条未认证的、缓慢的内存增长路径。
    ///
    /// 答案本身同样重要：对任意串都回 200 + 空集的话，调用方分不清
    /// "这个语言没有翻译"和"根本没有这个语言"。
    /// </summary>
    /// <param name="culture">文化名称（如 "en", "zh-CN"）</param>
    [HttpGet("resources/{culture}")]
    public virtual ApiResult<ResourceDto> GetResources(string culture)
    {
        Check.NotNullOrWhiteSpace(culture);

        var supported = ResolveSupportedCulture(culture);
        if (supported == null)
        {
            return Error<ResourceDto>($"Culture '{culture}' is not supported.", 400);
        }

        var localizer = _localizerFactory.Create(typeof(SharedResource));

        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = supported;
            var allStrings = localizer.GetAllStrings(includeParentCultures: true);

            var resources = new Dictionary<string, string>();
            foreach (var str in allStrings)
            {
                resources[str.Name] = str.Value;
            }

            return Ok(new ResourceDto
            {
                Culture = supported.Name,
                Resources = resources
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    /// <summary>
    /// 在受支持语言里找出与请求名匹配的那一个，找不到返回 null。
    ///
    /// 返回的是**名单里那个** <see cref="CultureInfo"/> 实例而不是新造一个：
    /// 匹配是大小写不敏感的，但资源文件名不是，所以查找必须用规范写法
    /// （<c>zh-CN</c>）而不是调用方送来的写法（<c>ZH-cn</c>）。
    /// </summary>
    /// <param name="culture">请求的文化名</param>
    private CultureInfo? ResolveSupportedCulture(string culture)
    {
        var options = _localizationOptions.Value;
        var candidates = options.SupportedUICultures ?? options.SupportedCultures;

        return candidates?.FirstOrDefault(c =>
            string.Equals(c.Name, culture, StringComparison.OrdinalIgnoreCase));
    }
}
