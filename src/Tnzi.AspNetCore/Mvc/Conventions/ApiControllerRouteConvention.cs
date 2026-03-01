
namespace Tnzi.AspNetCore.Mvc.Conventions;

/// <summary>
/// API 控制器路由提供者
/// 自动为 Controller 生成符合规范的路由模板
/// 
/// 重要: 实现 IApplicationModelProvider 而不是 IApplicationModelConvention
/// 因为 [ApiController] 的验证由 ApiBehaviorApplicationModelProvider (Order = -900) 执行
/// 必须在其之前设置路由，否则验证会失败
/// </summary>
public class ApiControllerRouteProvider : IApplicationModelProvider
{
    private readonly AspNetCoreOptions _options;

    /// <summary>
    /// 执行顺序，必须在 ApiBehaviorApplicationModelProvider (Order = -900) 之前执行
    /// </summary>
    public int Order => -1000;

    /// <summary>
    /// 初始化 API 控制器路由提供者
    /// </summary>
    /// <param name="options">ASP.NET Core 配置选项</param>
    public ApiControllerRouteProvider(AspNetCoreOptions options)
    {
        _options = Check.NotNull(options);
    }

    /// <summary>
    /// 在其他提供者执行后调用
    /// </summary>
    public void OnProvidersExecuted(ApplicationModelProviderContext context)
    {
        // 不需要实现
    }

    /// <summary>
    /// 在其他提供者执行前调用
    /// </summary>
    public void OnProvidersExecuting(ApplicationModelProviderContext context)
    {
        foreach (var controller in context.Result.Controllers)
        {
            // 如果 Controller 已经有 Route 特性,跳过(允许覆盖)
            if (HasExplicitRouteAttribute(controller))
            {
                continue;
            }

            // 自动生成路由模板
            var routeTemplate = GenerateRouteTemplate(controller.ControllerName);

            // 为 Controller 添加路由
            if (controller.Selectors.Count == 0)
            {
                controller.Selectors.Add(new SelectorModel());
            }

            foreach (var selector in controller.Selectors)
            {
                selector.AttributeRouteModel = new AttributeRouteModel
                {
                    Template = routeTemplate
                };
            }
        }
    }

    /// <summary>
    /// 检查 Controller 是否已有显式的 Route 特性
    /// 注意：只检查控制器类上的 [Route] 特性，不检查 Selectors 的状态
    /// 因为 Selectors 的路由可能是在后续的 Provider 或 Convention 中设置的
    /// </summary>
    private static bool HasExplicitRouteAttribute(ControllerModel controller)
    {
        // 检查控制器类上是否有 [Route] 特性（这是显式路由的唯一明确标志）
        var routeAttr = controller.Attributes.OfType<RouteAttribute>().FirstOrDefault();
        if (routeAttr != null)
        {
            return true;
        }

        // 如果没有显式 [Route] 特性，但 Selectors 中已经有非空的路由模板，
        // 可能是其他 Provider 或 Convention 设置的，也认为是显式路由
        // 注意：这可能会与 RoutePrefixConvention 产生冲突，但通常在 RoutePrefixConvention 执行前
        // ApiControllerRouteProvider 已经设置了路由，所以这个检查主要用于防止重复设置
        return controller.Selectors.Any(s => 
            s.AttributeRouteModel != null && 
            !string.IsNullOrWhiteSpace(s.AttributeRouteModel.Template));
    }

    /// <summary>
    /// 生成路由模板
    /// </summary>
    /// <param name="controllerName">控制器名称</param>
    /// <returns>路由模板</returns>
    private string GenerateRouteTemplate(string controllerName)
    {
        // 移除 "Controller" 后缀
        var name = controllerName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
            ? controllerName[..(controllerName.Length - "Controller".Length)]
            : controllerName;

        // 移除 "ControllerBase" 后缀 (用于抽象基类)
        if (name.EndsWith("Base", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..(name.Length - "Base".Length)];
        }

        // 根据配置的命名风格转换
        var routeName = _options.RouteNamingStyle switch
        {
            RouteNamingStyle.KebabCase => ToKebabCase(name),
            RouteNamingStyle.CamelCase => ToCamelCase(name),
            RouteNamingStyle.PascalCase => name,
            _ => ToKebabCase(name) // 默认使用 kebab-case
        };

        // 如果启用复数形式,转换为复数
        if (_options.UsePluralRoutes)
        {
            routeName = ToPlural(routeName);
        }

        return routeName;
    }

    /// <summary>
    /// 转换为复数形式
    /// 支持 kebab-case 和其他命名风格
    /// </summary>
    private static string ToPlural(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return word;
        }

        // 处理 kebab-case: user-detail -> user-details
        if (word.Contains('-'))
        {
            var parts = word.Split('-');
            var lastPart = parts[parts.Length - 1];
            parts[parts.Length - 1] = ToPluralWord(lastPart);
            return string.Join("-", parts);
        }

        // 处理普通单词
        return ToPluralWord(word);
    }

    /// <summary>
    /// 将单个单词转换为复数形式
    /// 实现简化的英文复数规则
    /// </summary>
    private static string ToPluralWord(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return word;
        }

        // 以 y 结尾(且前面不是元音字母): category -> categories
        if (word.Length > 1 &&
            word.EndsWith("y", StringComparison.OrdinalIgnoreCase) &&
            !IsVowel(word[word.Length - 2]))
        {
            return word[..(word.Length - 1)] + "ies";
        }

        // 以 fe 结尾: knife -> knives（必须在 f 之前检查）
        if (word.EndsWith("fe", StringComparison.OrdinalIgnoreCase))
        {
            return word[..(word.Length - 2)] + "ves";
        }

        // 以 f 结尾: leaf -> leaves
        if (word.EndsWith("f", StringComparison.OrdinalIgnoreCase))
        {
            return word[..(word.Length - 1)] + "ves";
        }

        // 以 s, x, z, ch, sh 结尾: bus -> buses, box -> boxes, church -> churches
        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("z", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
        {
            return word + "es";
        }

        // 默认: 直接加 s
        return word + "s";
    }

    /// <summary>
    /// 判断字符是否为元音字母
    /// </summary>
    private static bool IsVowel(char c)
    {
        return "aeiouAEIOU".IndexOf(c) >= 0;
    }

    /// <summary>
    /// 转换为 kebab-case (推荐的 RESTful 风格)
    /// 例如: UserProfile -> user-profile
    /// </summary>
    private static string ToKebabCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = new System.Text.StringBuilder();
        result.Append(char.ToLowerInvariant(input[0]));

        for (int i = 1; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsUpper(c))
            {
                result.Append('-');
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// 转换为 camelCase
    /// 例如: UserProfile -> userProfile
    /// </summary>
    private static string ToCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return char.ToLowerInvariant(input[0]) + input[1..];
    }
}