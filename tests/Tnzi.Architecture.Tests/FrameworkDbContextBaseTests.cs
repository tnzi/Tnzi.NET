using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Tnzi.EFCore;
using Tnzi.EFCore.Internal;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 架构门禁：框架程序集（<c>Tnzi.*</c>）里的每个 DbContext 都从框架的两个上下文基类之一派生，
/// 两个基类都在自己的 <c>OnConfiguring</c> 里经 <see cref="TnziDbContextHelper.OnConfiguring"/> 配置选项。
/// </summary>
/// <remarks>
/// <para>
/// 两个基类是平行的：<see cref="TnziDbContext{TDbContext}"/> 直接继承 EF 的 <see cref="DbContext"/>，
/// <see cref="Tnzi.Identity.Data.IdentityDbContext{TDbContext}"/> 继承 ASP.NET Core Identity 的上下文。
/// 只在其中一个上做的事，另一个就整个漏掉：PostgreSQL 的 U+0000 替换曾经只挂在 TnziDbContext 上，
/// 应用的主上下文（多半继承 IdentityDbContext）上一条带 NUL 的写入照旧以 SQLSTATE 22021 失败。
/// </para>
/// <para>
/// 新加一个不经这两个基类的框架 DbContext，或者新加第三个基类，都要在这里过一遍：
/// 让它的 <c>OnConfiguring</c> 调 <see cref="TnziDbContextHelper.OnConfiguring"/>，再把它登记进 <see cref="FrameworkBases"/>。
/// </para>
/// </remarks>
public class FrameworkDbContextBaseTests
{
    /// <summary>框架的上下文基类：每个都覆写 <c>OnConfiguring</c> 并调共享的配置。</summary>
    private static readonly Type[] FrameworkBases =
    [
        typeof(TnziDbContext<>),
        typeof(Tnzi.Identity.Data.IdentityDbContext<>)
    ];

    [Fact]
    public void FrameworkDbContexts_DeriveFromAFrameworkBase()
    {
        ArchitectureModuleGraph.Load();

        var contexts = AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsFrameworkAssembly)
            .SelectMany(GetTypesSafe)
            .Where(t => t.IsClass && typeof(DbContext).IsAssignableFrom(t))
            .Where(t => !IsFrameworkBase(t))
            .ToList();

        Assert.NotEmpty(contexts);

        var violations = contexts.Where(t => !DerivesFromFrameworkBase(t)).ToList();
        if (violations.Count > 0)
        {
            var report = string.Join(Environment.NewLine,
                violations.Select(t => $"  - {t.FullName} in {t.Assembly.GetName().Name}"));
            Assert.Fail(
                $"Found {violations.Count} framework DbContext type(s) that derive from neither TnziDbContext<> nor "
                + $"Tnzi.Identity.Data.IdentityDbContext<>, so they miss the shared options (the PostgreSQL U+0000 interceptor):"
                + $"{Environment.NewLine}{report}");
        }
    }

    [Fact]
    public void FrameworkBases_ConfigureTheirOptions()
    {
        foreach (var baseType in FrameworkBases)
        {
            var onConfiguring = baseType.GetMethod(
                "OnConfiguring",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                [typeof(DbContextOptionsBuilder)]);

            Assert.True(onConfiguring is not null,
                $"{baseType.FullName} must override OnConfiguring and call TnziDbContextHelper.OnConfiguring.");
        }
    }

    private static bool IsFrameworkBase(Type type)
        => type.IsGenericType && FrameworkBases.Contains(type.GetGenericTypeDefinition());

    private static bool DerivesFromFrameworkBase(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (IsFrameworkBase(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFrameworkAssembly(Assembly a)
    {
        var name = a.GetName().Name;
        return name is not null
            && name.StartsWith("Tnzi.", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".TestBase", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Type> GetTypesSafe(Assembly a)
    {
        try
        {
            return a.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Console.Error.WriteLine(
                $"[FrameworkDbContextBaseTests] {a.GetName().Name}: ReflectionTypeLoadException - {ex.Message}");
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
