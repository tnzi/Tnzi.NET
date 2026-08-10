using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 前后端 API 契约对账：<c>@tnzi/core</c> 请求的每个端点，后端都必须真的有。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么在这个项目里</b>：对账需要<b>全部</b>控制器，而这是唯一能拿到它们的地方 ——
/// <see cref="AllModulesStartupModule"/> 用必选 <c>[DependsOn]</c> 把 43 个模块全拉进了图，
/// 各模块自己的测试项目只引用自己那一个模块。
/// </para>
/// <para>
/// <b>它替代了什么</b>：CI 里曾有一步 "Check generated services up to date"，写成
/// <c>if [ -f src/Tnzi.UI/openapi.json ]; then pnpm codegen:check; else echo skip; fi</c>。
/// 那个文件在仓库里从不存在，所以这步<b>从未执行过一次</b>；再往下一层，<c>tnzi generate</c>
/// 还要求仓库有 <c>tnzi.json</c>，那个也不存在。也就是说前端 api 层的契约漂移长期无人把守，
/// 而 <c>packages/core/src/services/</c> 恰恰是<b>手工</b>跟着后端写的。
/// </para>
/// <para>
/// <b>方向是单向的</b>：只断言「前端调的端点后端有」，不断言反向。后端有端点而前端没接，
/// 是完全正常的状态（大量端点只给外部集成或尚未做管理页），拿它当失败会让门禁天天红。
/// 反向缺口由 <c>/iterate</c> 的纵切分析处理（「后端有能力但消费方到不了」），不在这里。
/// </para>
/// </remarks>
public class FrontendBackendContractTests
{
    /// <summary>路由参数占位符归一化：<c>{id}</c> / <c>{id:guid}</c> / <c>{*path}</c> 一律成 <c>{}</c>。</summary>
    private static readonly Regex RouteParam = new(@"\{[^}]*\}", RegexOptions.Compiled);

    /// <summary>
    /// 前端每一个 API 调用点，都必须命中一个真实存在的后端端点。
    /// </summary>
    [Fact]
    public void EveryFrontendApiCall_TargetsAnExistingBackendEndpoint()
    {
        var repoRoot = RepoRoot.Locate();
        var scan = FrontendApiScanner.Scan(repoRoot);
        var backend = BackendRoutes();

        var orphans = scan.Calls
            .Where(c => IsOrphan(c, backend))
            .OrderBy(c => c.File, StringComparer.Ordinal)
            .ThenBy(c => c.Line)
            .ToList();

        if (orphans.Count > 0)
        {
            var detail = string.Join('\n', orphans.Select(o =>
                $"  {o} — 后端无 {string.Join(" 或 ", o.AcceptableRoutes())}"));

            Assert.Fail(
                $"{orphans.Count} 个前端 API 调用指向后端不存在的端点（运行时会 404）：\n{detail}\n\n"
                + "修法二选一：把前端路径改成后端真实路由，或在后端补上该端点。"
                + "若端点确实已废弃，删掉前端调用而不是放宽本门禁。");
        }
    }

    /// <summary>
    /// 每一个 <c>client.*</c> 调用点都必须能被解析出路径。
    /// </summary>
    /// <remarks>
    /// 没有这条，上面那条门禁的覆盖面就是不可知的：解析器悄悄漏掉一批调用点，
    /// 「没有孤儿」与「没检查」长得一模一样。api.ts 里新出现一种路径写法时，
    /// 这里会先红，提示去扩展 <see cref="FrontendApiScanner"/>。
    /// </remarks>
    [Fact]
    public void EveryClientCallSite_IsParseable()
    {
        var repoRoot = RepoRoot.Locate();
        var scan = FrontendApiScanner.Scan(repoRoot);

        if (scan.Unparsed.Count > 0)
        {
            Assert.Fail(
                $"{scan.Unparsed.Count}/{scan.TotalCallSites} 个 client.* 调用点解析不出请求路径，"
                + "它们目前不受契约对账约束：\n  "
                + string.Join("\n  ", scan.Unparsed)
                + "\n\n扩展 FrontendApiScanner 支持这种写法，或把该调用点改成既有写法。");
        }

        // 至少一条：工厂体内的调用点会按实参组数展开成多条（一个方法服务 8 种单据），
        // 所以这里是下界而不是相等 —— 写成相等会因为「解析得更全」而失败。
        scan.Calls.Count.ShouldBeGreaterThanOrEqualTo(scan.TotalCallSites);
    }

    /// <summary>
    /// 防锈：两侧扫描规模都必须维持在下界之上。
    /// </summary>
    /// <remarks>
    /// 用下界而不是 <c>ShouldNotBeEmpty</c>：正则一旦退化到只匹配几个调用点，
    /// 「所有前端调用都有对应端点」照样成立，整条门禁会安静失效。数字取当前实测的
    /// 保守下界（前端 877 个调用点 / 18 个 api.ts / 后端 145 个控制器文件），
    /// 正常增删只会让它更宽松。
    /// </remarks>
    [Fact]
    public void ContractScan_CoversExpectedScale()
    {
        var repoRoot = RepoRoot.Locate();
        var scan = FrontendApiScanner.Scan(repoRoot);
        scan.TotalCallSites.ShouldBeGreaterThanOrEqualTo(700,
            $"只扫到 {scan.TotalCallSites} 个前端 API 调用点，远少于预期 —— 是扫描坏了，不是调用真的变少了");

        var backend = BackendRoutes();
        // 实测 1078 条端点（145 个控制器类），下界取 900。
        backend.Count.ShouldBeGreaterThanOrEqualTo(900,
            $"只反射到 {backend.Count} 个后端端点，远少于预期 —— 可能是模块图没加载全或反射条件写窄了");
    }

    /// <summary>
    /// 正反样本：证明<b>对账逻辑本身</b>真能分辨端点存在与否。
    /// </summary>
    /// <remarks>
    /// 没有这条，「零孤儿」也可能只是因为匹配恒真（归一化把两边抹成同一个串、
    /// <c>Any</c> 的谓词写反、动词放宽扩大到全集）。
    /// <para>
    /// ★ 这条测试的第一版只断言了 <c>BackendRoutes()</c> 集合的内容
    /// （<c>ShouldContain("GET /files/{}")</c>），**根本没走匹配路径** —— 名字叫
    /// Matching_Rejects 却测的是 BackendRoutes contains。匹配逻辑写错时它照样绿，
    /// 也就是一条声称防锈却不防锈的测试。现在改成拿真实的 <see cref="FrontendApiScanner.Call"/>
    /// 过一遍与主门禁完全相同的判定表达式。
    /// </para>
    /// </remarks>
    [Fact]
    public void Matching_DistinguishesExistingFromMissingEndpoints()
    {
        var backend = BackendRoutes();

        // 真实存在的一条（DefaultStorageController: [Route("files")] + [HttpGet("{id}")]）
        var real = new FrontendApiScanner.Call("probe.ts", 1, "get", "/files/{}");
        var missing = new FrontendApiScanner.Call("probe.ts", 2, "get", "/files/{}/nope");

        // 与主门禁逐字相同的判定
        IsOrphan(real, backend).ShouldBeFalse("真实存在的端点被判成了孤儿");
        IsOrphan(missing, backend).ShouldBeTrue("不存在的端点没有被判成孤儿 —— 对账逻辑恒真");

        // 动词也要参与判定：/files/{} 上有 GET，但没有 PATCH
        IsOrphan(real with { Method = "patch" }, backend)
            .ShouldBeTrue("动词没有参与匹配 —— PATCH /files/{} 并不存在却通过了");

        // 放宽过的方法反过来必须能命中 POST-only 的流式端点
        var streaming = new FrontendApiScanner.Call("probe.ts", 3, "resolveUrl", "/chat/stream");
        backend.ShouldContain("POST /chat/stream");
        backend.ShouldNotContain("GET /chat/stream");
        IsOrphan(streaming, backend).ShouldBeFalse("resolveUrl 的动词放宽没生效");
    }

    private static bool IsOrphan(FrontendApiScanner.Call call, HashSet<string> backend)
        => !call.AcceptableRoutes().Any(backend.Contains);

    /// <summary>反射出全部控制器端点，形如 <c>"GET /files/{}"</c>。</summary>
    private static HashSet<string> BackendRoutes()
    {
        var routes = new HashSet<string>(StringComparer.Ordinal);

        var assemblies = ArchitectureModuleGraph.Load()
            .Modules
            .Select(m => m.Type.Assembly)
            .Distinct()
            .ToList();

        foreach (var assembly in assemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract || !typeof(ControllerBase).IsAssignableFrom(type))
                    continue;

                var controllerTemplate = type.GetCustomAttributes<RouteAttribute>(inherit: true)
                    .Select(a => a.Template)
                    .FirstOrDefault();

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    foreach (var http in method.GetCustomAttributes<HttpMethodAttribute>(inherit: true))
                    {
                        var template = Combine(controllerTemplate, http.Template);
                        foreach (var verb in http.HttpMethods)
                            routes.Add($"{verb.ToUpperInvariant()} {template}");
                    }
                }
            }
        }

        return routes;
    }

    /// <summary>
    /// 组合类级与方法级路由模板。
    /// </summary>
    /// <remarks>
    /// 刻意<b>不</b>加 <c>api/</c> 前缀：那是 <c>RoutePrefixConvention</c> 在运行时按
    /// <c>AspNetCore:ApiPathPrefix</c> 加的，而前端 HttpClient 的 baseUrl 也带着它 ——
    /// 两边都不含前缀，对账口径才一致。方法级模板以 <c>/</c> 或 <c>~/</c> 开头时是绝对路由，
    /// 覆盖类级模板（ASP.NET Core 语义）。
    /// </remarks>
    private static string Combine(string? controllerTemplate, string? methodTemplate)
    {
        var method = methodTemplate?.Trim() ?? string.Empty;
        if (method.StartsWith("~/", StringComparison.Ordinal))
            return NormalizeRoute(method[1..]);
        if (method.StartsWith('/'))
            return NormalizeRoute(method);

        var controller = controllerTemplate?.Trim().Trim('/') ?? string.Empty;
        var combined = method.Length == 0 ? controller : $"{controller}/{method}";
        return NormalizeRoute(combined);
    }

    private static string NormalizeRoute(string template)
        => FrontendApiScanner.Normalize(RouteParam.Replace(template, "{}"));

    /// <summary>
    /// 取程序集里能加载的类型。
    /// </summary>
    /// <remarks>
    /// 不用 <c>catch { return []; }</c> 把整个程序集丢掉：那会让「这个程序集的控制器一个都没扫到」
    /// 与「它真的没有控制器」不可区分，而后者是正常状态（基础设施模块本来就没有控制器）。
    /// 部分类型加载失败时保留成功的那部分，规模下界断言会兜住大面积失败。
    /// </remarks>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }
}
