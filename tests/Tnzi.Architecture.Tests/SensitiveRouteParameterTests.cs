using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Tnzi.Security;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 架构门禁：<b>路由模板里装凭据的参数，名字必须在敏感名单里</b>，否则它的值会原样进审计表、访问日志与请求日志。
/// </summary>
/// <remarks>
/// <para>
/// ★★ 路径里的令牌就是能用的凭据：签署链接 <c>signing/{token}</c> 拿到就能以收件人的名义签字，
/// 分享链接 <c>files/share/{token}</c> 拿到就能下载。审计 / 访问日志 / 请求日志写路径时经
/// <c>HttpContext.GetRedactedPath()</c> 把<b>名字</b>命中 <see cref="QueryStringRedactor.DefaultSensitiveKeys"/>
/// 的路由参数换成掩码：判定只看名字，所以一个新端点把令牌放进 <c>{inviteToken}</c>、<c>{secret}</c>
/// 这类名单外的名字，就会一声不响地重新开始往日志表里写能用的链接。
/// </para>
/// <para>
/// 判据刻意窄：只看名字里带凭据词的参数（token / secret / password / apikey / signature / sig）。
/// 业务编码 <c>{code}</c>、配置键 <c>{key}</c> 不在此列：把它们掩掉会让审计行答不出「改的是哪一个」。
/// </para>
/// </remarks>
public class SensitiveRouteParameterTests
{
    private static readonly Regex RouteParameter = new(@"\{\**([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private static readonly Regex CredentialLikeName = new(
        "token|secret|password|apikey|api_key|signature|^sig$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void EveryCredentialLikeRouteParameter_IsInTheSensitiveKeyList()
    {
        var offenders = CredentialLikeRouteParameters()
            .Where(p => !QueryStringRedactor.IsSensitive(p.Name, QueryStringRedactor.DefaultSensitiveKeys))
            .Select(p => $"{p.Controller}: {{{p.Name}}} in \"{p.Template}\"")
            .Distinct()
            .ToList();

        offenders.ShouldBeEmpty(
            "以下路由参数看起来装的是凭据，但名字不在 QueryStringRedactor.DefaultSensitiveKeys 里，\n"
            + "审计 / 访问日志 / 请求日志会把它的值原样写下来。把参数改名为名单里的名字（通常是 token），\n"
            + "或在确属凭据时把名字加进名单（它同时管查询串脱敏）。\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>对照组：门禁真的看得见框架自己的令牌端点：否则一个什么都扫不到的实现也会全绿。</summary>
    [Fact]
    public void Scan_CoversTheKnownTokenEndpoints()
    {
        var controllers = CredentialLikeRouteParameters().Select(p => p.Controller).ToHashSet(StringComparer.Ordinal);

        controllers.ShouldContain("DefaultSigningController");
        controllers.ShouldContain("DefaultStorageController");
    }

    private sealed record RouteParameterUse(string Controller, string Template, string Name);

    private static IEnumerable<RouteParameterUse> CredentialLikeRouteParameters()
    {
        foreach (var controller in ControllerTypes())
        {
            var templates = controller.GetCustomAttributes<RouteAttribute>(inherit: true).Select(a => a.Template)
                .Concat(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Select(a => a.Template)
                        .Concat(m.GetCustomAttributes<RouteAttribute>(inherit: true).Select(a => a.Template))))
                .OfType<string>();

            foreach (var template in templates)
            {
                foreach (Match match in RouteParameter.Matches(template))
                {
                    var name = match.Groups[1].Value;
                    if (CredentialLikeName.IsMatch(name))
                    {
                        yield return new RouteParameterUse(controller.Name, template, name);
                    }
                }
            }
        }
    }

    private static IEnumerable<Type> ControllerTypes()
    {
        // 先把整个模块图装起来，让每个 Tnzi.* 程序集都真的被加载进当前 AppDomain。
        ArchitectureModuleGraph.Load();

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsFrameworkAssembly)
            .SelectMany(SafeGetTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));
    }

    private static bool IsFrameworkAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return name is not null
            && name.StartsWith("Tnzi.", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".TestBase", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
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
