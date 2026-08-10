using System.Reflection;
using Tnzi.Identity.Controllers;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 自助资料端点的越权面：入参类型里不能有特权字段，端点也不能改回收管理端 DTO。
/// </summary>
/// <remarks>
/// <para>
/// 锁的是一条已发生的提权路径：<c>PUT /api/users/profile</c> 此前直接收
/// <see cref="UpdateUserDto"/>（管理端 DTO，带 <c>RoleIds</c> 与 <c>OrganizationId</c>），
/// 并原样交给 <c>UserService.UpdateAsync</c> —— 而那里见到 <c>RoleIds != null</c> 就执行角色增删。
/// 于是<b>任意已登录用户</b> PUT 一个 <c>{"roleIds":["&lt;管理员角色id&gt;"]}</c> 即可自助提权；
/// <c>organizationId</c> 更是连一层校验都没有，可以把自己挪进任意组织绕过组织级数据域过滤。
/// </para>
/// <para>
/// <b>为什么用反射锁形状而不是发一个请求</b>：这条缺陷正好落在
/// <c>UserService.UpdateAsync</c> 那 27 个测试的盲区里 —— 服务本身的行为是<b>对的</b>
/// （管理端就该能改角色），错的是「自助控制器把管理端 DTO 交给了它」这个接线。
/// 没有控制器级测试就没有任何东西看得见这件事。形状断言是这里最直接的锚点。
/// </para>
/// </remarks>
public class UserProfileDtoShapeTests
{
    /// <summary>只有管理员能改的字段。加自助字段请加进 <see cref="UpdateProfileDto"/>。</summary>
    private static readonly string[] PrivilegedFields = ["RoleIds", "OrganizationId"];

    [Fact]
    public void UpdateProfileDto_ExposesNoPrivilegedField()
    {
        var exposed = typeof(UpdateProfileDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Intersect(PrivilegedFields)
            .ToList();

        exposed.ShouldBeEmpty(
            $"自助资料 DTO 暴露了特权字段 {string.Join(", ", exposed)} —— "
            + "字段一旦能被绑定，任意已登录用户就能给自己授予角色或改所属组织");
    }

    [Fact]
    public void ProfileUpdateEndpoint_BindsTheSelfServiceDto()
    {
        var action = typeof(DefaultUserProfileController)
            .GetMethod(nameof(DefaultUserProfileController.UpdateCurrentUser));

        action.ShouldNotBeNull();
        var parameter = action.GetParameters().Single();

        parameter.ParameterType.ShouldBe(typeof(UpdateProfileDto),
            "自助端点必须绑定 UpdateProfileDto；换回管理端的 UpdateUserDto 会让 roleIds "
            + "重新变成一个可以从请求体里传进来的字段");
    }

    /// <summary>
    /// 管理端 DTO 仍然<b>应该</b>带这两个字段 —— 否则管理员就改不了角色了。
    /// </summary>
    /// <remarks>
    /// 这条防的是「为了修提权而把字段从 <see cref="UpdateUserDto"/> 上删掉」这种过度修复：
    /// 那样自助路径确实安全了，但管理端的角色分配会静默失效（`RoleIds` 恒为 null ⇒ 那段增删不执行）。
    /// </remarks>
    [Fact]
    public void UpdateUserDto_KeepsPrivilegedFieldsForTheAdminSurface()
    {
        var names = typeof(UpdateUserDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        foreach (var field in PrivilegedFields)
        {
            names.ShouldContain(field,
                $"管理端 DTO 丢了 {field}，管理员将无法改角色/组织，而这段逻辑失效是静默的");
        }
    }
}
