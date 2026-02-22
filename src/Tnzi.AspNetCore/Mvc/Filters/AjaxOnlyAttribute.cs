namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// Ajax限制特性，限制操作只能通过 Ajax 请求访问
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AjaxOnlyAttribute : Attribute
{
}

