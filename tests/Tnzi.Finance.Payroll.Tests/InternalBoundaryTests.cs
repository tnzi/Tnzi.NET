using System.Reflection;
using Tnzi.TestBase;

namespace Tnzi.Finance.Payroll.Tests;

/// <summary>
/// Finance 子模块边界约定：消费方生产代码只消费 Finance 的公共扩展面，
/// 不得引用 <c>Tnzi.Finance.Services.Internal</c> 命名空间。
/// </summary>
/// <remarks>
/// 该命名空间下经 DI 注入 public 服务构造函数的协作类（LedgerPostingEngine 等）
/// 受 CS0051 与容器仅解析 public 构造函数的限制无法降为 internal，
/// 编译器管不到的这部分边界由本测试强制。扫描面 = 类型签名级引用
/// （基类 / 接口 / 字段 / 属性 / 方法与构造参数 / 返回类型 / 泛型实参），
/// 覆盖构造注入与成员持有这两种真实的越界形态；方法体内的临时解析
/// （如 <c>GetRequiredService&lt;T&gt;()</c> 局部变量）不在扫描面。
/// 扫描集按程序集参数化：将来 Tnzi.Finance.Banking 等 Finance 系子模块
/// 加入 <see cref="TargetAssemblies"/> 即被覆盖；Tnzi.Hosting / Acme 等
/// 其余引用方不在本项目引用闭包内（当前经人工 grep 证实零引用）。
/// </remarks>
public class InternalBoundaryTests
{
    private const string ForbiddenNamespace = "Tnzi.Finance.Services.Internal";

    private static readonly Assembly[] TargetAssemblies =
    [
        typeof(PayrollModule).Assembly,
    ];

    [Fact]
    public void FinanceConsumers_DoNotReference_FinanceServicesInternal()
    {
        var violations = new List<string>();

        foreach (var type in TargetAssemblies.SelectMany(a => a.SafeGetTypes()))
        {
            InspectType(type, violations);
        }

        violations.ShouldBeEmpty(
            $"Finance consumers must use the public surface only; found references to {ForbiddenNamespace}: "
            + string.Join("; ", violations));
    }

    private static void InspectType(Type type, List<string> violations)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        Check(type.BaseType, $"{type.FullName} base type", violations);
        foreach (var itf in type.GetInterfaces())
            Check(itf, $"{type.FullName} interface", violations);

        foreach (var field in type.GetFields(all))
            Check(field.FieldType, $"{type.FullName}.{field.Name} field", violations);

        foreach (var property in type.GetProperties(all))
            Check(property.PropertyType, $"{type.FullName}.{property.Name} property", violations);

        foreach (var ctor in type.GetConstructors(all))
            foreach (var parameter in ctor.GetParameters())
                Check(parameter.ParameterType, $"{type.FullName} ctor parameter '{parameter.Name}'", violations);

        foreach (var method in type.GetMethods(all))
        {
            Check(method.ReturnType, $"{type.FullName}.{method.Name} return type", violations);
            foreach (var parameter in method.GetParameters())
                Check(parameter.ParameterType, $"{type.FullName}.{method.Name} parameter '{parameter.Name}'", violations);
        }
    }

    private static void Check(Type? type, string site, List<string> violations)
    {
        if (type == null)
            return;

        if (type.IsArray || type.IsByRef || type.IsPointer)
        {
            Check(type.GetElementType(), site, violations);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                Check(argument, site, violations);
        }

        if (type.Namespace == ForbiddenNamespace)
            violations.Add($"{site} -> {type.Name}");
    }
}
