namespace Tnzi.Localization.Controllers;

/// <summary>
/// 本地化 API 控制器
/// 提供语言列表和资源查询接口
/// </summary>
[ApiController]
[Route("localization")]
[ApiExplorerSettings(GroupName = "system")]
[AllowAnonymous]
public class LocalizationController : ApiControllerBase
{
    private readonly IOptions<RequestLocalizationOptions> _localizationOptions;
    private readonly IStringLocalizerFactory _localizerFactory;

    public LocalizationController(IOptions<RequestLocalizationOptions> localizationOptions, IStringLocalizerFactory localizerFactory)
    {
        _localizationOptions = Check.NotNull(localizationOptions);
        _localizerFactory = Check.NotNull(localizerFactory);
    }

    /// <summary>
    /// 获取支持的语言列表
    /// </summary>
    [HttpGet("cultures")]
    public ApiResult<CultureListDto> GetCultures()
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
    /// 获取指定语言的所有资源
    /// </summary>
    /// <param name="culture">文化名称（如 "en", "zh-CN"）</param>
    [HttpGet("resources/{culture}")]
    public ApiResult<ResourceDto> GetResources(string culture)
    {
        Check.NotNullOrWhiteSpace(culture);

        // 使用 SharedResource 类型获取本地化器
        var localizer = _localizerFactory.Create(typeof(SharedResource));

        // 临时切换文化以获取指定语言的资源
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var allStrings = localizer.GetAllStrings(includeParentCultures: true);

            var resources = new Dictionary<string, string>();
            foreach (var str in allStrings)
            {
                resources[str.Name] = str.Value;
            }

            return Ok(new ResourceDto
            {
                Culture = culture,
                Resources = resources
            });
        }
        catch (CultureNotFoundException)
        {
            return Error<ResourceDto>($"Culture '{culture}' is not valid.", 400);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }
}
