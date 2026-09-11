using System.Reflection;
using Tnzi.Domain.Entities;
using Tnzi.Security;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 架构门禁：<b>一次性凭据实体必须退出实体级审计</b>。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>为什么需要门禁。</b><c>Tnzi.Audit</c> 的实体级审计默认开启，并且是
/// <b>无差别采集</b>的：除了五个自审计/基础设施类型和打了 <see cref="AuditIgnoreAttribute"/>
/// 的以外，任何被 EF 跟踪到的实体变更都会把属性值写进 <c>Audit_PropertyEntry</c>。
/// 脱敏靠 <c>AuditOptions.SensitiveFields</c>，而它是按属性名<b>精确匹配</b>的<b>跨实体</b>名单 ——
/// 于是 <c>Code</c> / <c>Value</c> 这类通用名<b>进不了名单</b>（收录 <c>Code</c> 会掩掉全仓所有
/// 科目代码、货币代码、模块编码）。拦截器自己的注释写着这一条，但没有任何东西去兑现它。
/// </para>
/// <para>
/// 实发形态：<c>TwoFactorCode</c> 没有这个特性，于是每次发码都把<b>明文验证码</b>连同收件地址
/// 写进审计表 —— 一个只读的 <c>audit.operation.view</c> 就足以给任意账号发一枚找回密码的码、
/// 再从审计明细里读出来。只读权限直接升级为账号接管。
/// </para>
/// <para>
/// ★ <b>判据刻意收得很窄</b>：只认「名字是凭据词 + 实体带一次性凭据的形状（有过期时间或已用标记）」，
/// 或者名字本身已经毫无歧义（<c>Secret</c> / <c>Password</c> / <c>ApiKey</c>）。
/// 宽判据会把几十个正当的业务 <c>Code</c> 列进来，逼出一份人人往里追加的 allowlist ——
/// 那种 allowlist 存在的第二天就不再是一个决定了。
/// </para>
/// </remarks>
public class CredentialAuditIgnoreTests
{
    /// <summary>名字本身就说明是凭据的属性名（不看实体形状，直接要求豁免）。</summary>
    private static readonly HashSet<string> UnambiguousCredentialNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Secret", "ApiKey", "ClientSecret", "PrivateKey", "SecurityStamp",
    };

    /// <summary>
    /// 有歧义、要结合实体形状才算凭据的属性名。
    /// </summary>
    /// <remarks>
    /// <c>Code</c> 在业务实体上遍地都是（科目代码、货币代码、模块编码）；
    /// 只有当这个实体<b>同时</b>带着「过期时间」或「已用标记」时，它才是一枚一次性凭据。
    /// </remarks>
    private static readonly HashSet<string> ShapeDependentCredentialNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "Token", "Value",
    };

    /// <summary>一次性凭据的形状标记：有其一即认为该实体承载短命凭据。</summary>
    private static readonly HashSet<string> OneTimeShapeMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "ExpiresAt", "ExpiresAtUtc", "IsUsed", "UsedAt", "ConsumedAt",
    };

    [Fact]
    public void EveryOneTimeCredentialProperty_IsExemptFromEntityAudit()
    {
        var offenders = new List<string>();

        foreach (var entityType in EntityTypes())
        {
            if (entityType.IsDefined(typeof(AuditIgnoreAttribute), inherit: true))
            {
                continue;   // 类级豁免，整个实体都不采集
            }

            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var hasOneTimeShape = properties.Any(p => OneTimeShapeMarkers.Contains(p.Name));

            foreach (var property in properties)
            {
                if (property.PropertyType != typeof(string))
                {
                    continue;   // 凭据都是字符串；数值型的 Code 是业务编号
                }

                var isCredential =
                    UnambiguousCredentialNames.Contains(property.Name)
                    || (hasOneTimeShape && ShapeDependentCredentialNames.Contains(property.Name));

                if (!isCredential)
                {
                    continue;
                }

                // ★ 判据是「被**任一**机制盖住了」，不是「有没有那个特性」。两种机制各有射程：
                //   · [AuditIgnore] —— 精确到这一个声明，属性完全不进审计行。通用名只能用它。
                //   · SensitiveFields —— 按属性名跨实体掩码（记「变了」但值打码）。
                //     它是唯一能盖住**继承来的**属性的办法：User.SecurityStamp 定义在
                //     ASP.NET Identity 的 IdentityUser<TKey> 上，我们打不了特性。
                var covered = property.IsDefined(typeof(AuditIgnoreAttribute), inherit: true)
                              || RequestBodyRedactor.DefaultSensitiveFields.Contains(property.Name);

                if (!covered)
                {
                    offenders.Add($"{entityType.FullName}.{property.Name}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "以下属性是（或看起来是）一次性凭据，却会被实体级审计原样写进 Audit_PropertyEntry。\n"
            + "AuditOptions.SensitiveFields 是按属性名跨实体匹配的，通用名进不了那份名单 ——\n"
            + "请给属性或整个实体打 [AuditIgnore]（短命凭据建议打在类上：Address/Purpose 这类\n"
            + "旁证字段合起来同样是情报）。若确属误判，把判据收窄，不要往门禁里加例外。\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 对照组：门禁真的能看见东西 —— 否则一个扫不到任何实体的实现也会「全绿」。
    /// </summary>
    [Fact]
    public void Scan_CoversKnownCredentialEntities()
    {
        var entityNames = EntityTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        entityNames.ShouldContain("TwoFactorCode");
        entityNames.ShouldContain("AuthToken");
    }

    /// <summary>全模块图里所有框架程序集的实体类型。</summary>
    private static IEnumerable<Type> EntityTypes()
    {
        // 先把整个模块图装起来，让每个 Tnzi.* 程序集都真的被加载进当前 AppDomain ——
        // 否则扫描面取决于「测试项目恰好引用了谁」，那是一个会随时间静默缩小的面。
        ArchitectureModuleGraph.Load();

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsFrameworkAssembly)
            .SelectMany(SafeGetTypes)
            .Where(IsEntityType);
    }

    private static bool IsFrameworkAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return name is not null
            && name.StartsWith("Tnzi.", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".TestBase", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEntityType(Type type)
        => type is { IsClass: true, IsAbstract: false }
           && typeof(IEntity).IsAssignableFrom(type);

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
