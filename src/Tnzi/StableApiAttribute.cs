namespace Tnzi;

/// <summary>
/// 标记为稳定 API：遵循语义化版本控制，主版本号不变不会有破坏性变更。
/// </summary>
/// <remarks>
/// 本特性表达的是「稳定性意图」——面向使用者的 SemVer 承诺，供文档与工具展示，属人工声明，不做机器校验。
/// 「公共 API 是否发生意外破坏性变更」由 <c>Microsoft.CodeAnalysis.PublicApiAnalyzers</c> 机器门禁强制执行：
/// 每个受管项目的 <c>PublicAPI.Shipped.txt</c> 记录已冻结的公共 API 面，新增未登记 API 触发 RS0016、
/// 删除已登记 API 触发 RS0017（在 Tnzi / Tnzi.EFCore / Tnzi.AspNetCore 中提级为编译错误）。
/// 分工：本特性负责「意图」，analyzer 负责「事实校验」。详见 docs/coding-standards/public-api-governance.md。
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
public sealed class StableApiAttribute : Attribute
{
    /// <summary>
    /// 从哪个版本开始稳定
    /// </summary>
    public string? Since { get; set; }
}
