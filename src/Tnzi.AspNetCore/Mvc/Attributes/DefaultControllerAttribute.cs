namespace Tnzi.AspNetCore.Mvc;

/// <summary>
/// 标记模块提供的默认 Controller
/// 当 HostingModule 未加载时，带此标记的 Controller 会被 <see cref="Conventions.DefaultControllerFilterProvider"/> 自动排除。
/// 当用户注册了同路由的 Controller 时，带此标记的 Controller 会被 <see cref="Conventions.ModuleControllerReplacementProvider"/> 自动让位。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DefaultControllerAttribute : Attribute
{
}
