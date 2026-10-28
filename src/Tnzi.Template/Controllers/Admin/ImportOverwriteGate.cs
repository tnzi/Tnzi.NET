namespace Tnzi.Template.Controllers.Admin;

/// <summary>
/// 导入时覆盖已有记录的那一半权限：<c>overwriteExisting = true</c> 会改写已有的模板 / 布局，等同于更新。
/// </summary>
/// <remarks>
/// 导入端点的特性只要求 <c>.create</c>（导入的本意是新建），而覆盖开关在请求体里，特性按不到它，所以在动作里命令式地再问一次
/// <c>.update</c>。少了这一问，只被授予新建的角色带上 <c>overwriteExisting</c> 就能改写每一条已有模板，绕过
/// <c>PUT</c> 端点要求的那个码；模板是在服务进程里编译执行的 Razor，<c>.update</c> 正是代码执行的授权。
/// 授权模块没加载（没有权限检查器）时同样拒绝，与 deny-by-default 一致。
/// </remarks>
internal static class ImportOverwriteGate
{
    public static async Task RequireAsync(IServiceProvider services, string updatePermission)
    {
        var checker = services.GetService<IPermissionChecker>();
        if (checker == null)
        {
            throw new ForbiddenException(
                "Permission checker is not available. Please ensure the Authorization module is loaded.",
                ErrorCodes.FORBIDDEN);
        }

        if (!await checker.IsGrantedAsync(updatePermission))
        {
            throw new ForbiddenException(
                $"Overwriting existing records on import requires the '{updatePermission}' permission.",
                ErrorCodes.FORBIDDEN);
        }
    }
}
