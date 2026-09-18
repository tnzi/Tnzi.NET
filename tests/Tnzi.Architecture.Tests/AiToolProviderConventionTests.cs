using System.Reflection;
using System.Text.RegularExpressions;
using Tnzi.AI.Tools;
using Tnzi.AI.Tools.Attributes;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// Architecture gate: every concrete class that declares <c>[AIToolGroup]</c> MUST also implement
/// <see cref="IAIToolProvider"/>. <c>ToolScanner</c> discovers tool providers by requiring BOTH
/// (interface AND attribute); a class carrying only the attribute is silently skipped, so its whole
/// tool group never reaches <c>IToolRegistry</c> and the model can never see those tools.
/// </summary>
/// <remarks>
/// 这不是假想：<c>task</c> / <c>todo</c> / <c>clarification</c> / <c>artifact</c> 四个工具组从
/// 首次提交起就只带特性不带接口，六个月里从未进过注册表 —— 而检测它们调用的中间件、门住它们的
/// 权限码、排除它们的子 Agent 模板都在对着一个不存在的东西工作。既有测试全部直接 <c>new</c>
/// 这些类，没有一条走扫描器，所以一直是绿的。
/// </remarks>
public class AiToolProviderConventionTests
{
    [Fact]
    public void ClassesDeclaringAIToolGroup_MustImplementIAIToolProvider()
    {
        ArchitectureModuleGraph.Load();

        var violations = AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsFrameworkAssembly)
            .SelectMany(GetTypesSafe)
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .Where(t => t.GetCustomAttribute<AIToolGroupAttribute>() != null)
            .Where(t => !typeof(IAIToolProvider).IsAssignableFrom(t))
            .ToList();

        if (violations.Count > 0)
        {
            var report = string.Join(Environment.NewLine,
                violations.Select(t =>
                    $"  - {t.FullName} in {t.Assembly.GetName().Name} declares [AIToolGroup(\"{t.GetCustomAttribute<AIToolGroupAttribute>()!.GroupName}\")] but does not implement IAIToolProvider (ToolScanner will never register its tools)"));
            Assert.Fail(
                $"Found {violations.Count} tool-group class(es) invisible to ToolScanner:{Environment.NewLine}{report}");
        }
    }

    /// <summary>
    /// 每个框架内建工具必须显式声明 snake_case 的工具名。<c>AIFunctionAttribute(string)</c> 单参构造收的是
    /// <b>描述</b>不是名字：<c>[AIFunction("spawn_agent", Description = "...")]</c> 能编译，但 Name 为 null，
    /// 扫描器退回方法名登记成 <c>SpawnAgent</c>，而中间件、子 Agent 名单、权限规则全按 <c>spawn_agent</c> 找它 ——
    /// 十个协议工具此前正是这样在名字上第二次失联的。
    /// </summary>
    [Fact]
    public void FrameworkAIFunctions_MustDeclareExplicitSnakeCaseNames()
    {
        ArchitectureModuleGraph.Load();

        var violations = AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsFrameworkAssembly)
            .SelectMany(GetTypesSafe)
            .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<AIToolGroupAttribute>() != null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(m => (Type: t, Method: m, Attr: m.GetCustomAttribute<AIFunctionAttribute>()))
                .Where(x => x.Attr != null))
            .Where(x => x.Attr!.Name == null || !SnakeCase.IsMatch(x.Attr.Name))
            .ToList();

        if (violations.Count > 0)
        {
            var report = string.Join(Environment.NewLine,
                violations.Select(v =>
                    $"  - {v.Type.FullName}.{v.Method.Name}: [AIFunction] Name is '{v.Attr!.Name ?? "<null>"}' (use the (name, description) constructor with a snake_case name)"));
            Assert.Fail(
                $"Found {violations.Count} AI function(s) without an explicit snake_case name:{Environment.NewLine}{report}");
        }
    }

    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// 反向守卫：门禁扫描面必须真的包含带 <c>[AIToolGroup]</c> 的类，否则上面那条对空集恒绿。
    /// </summary>
    [Fact]
    public void Scan_CoversBuiltInToolGroups()
    {
        ArchitectureModuleGraph.Load();

        var groups = AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsFrameworkAssembly)
            .SelectMany(GetTypesSafe)
            .Select(t => t.GetCustomAttribute<AIToolGroupAttribute>()?.GroupName)
            .Where(g => g != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        new[] { "task", "todo", "clarification", "artifact", "websearch", "sandbox" }.ShouldBeSubsetOf(groups);
    }

    private static bool IsFrameworkAssembly(Assembly a)
    {
        var name = a.GetName().Name;
        return name is not null
            && name.StartsWith("Tnzi", StringComparison.OrdinalIgnoreCase)
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
                $"[AiToolProviderConventionTests] {a.GetName().Name}: ReflectionTypeLoadException - {ex.Message}");
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
