using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Tnzi.AI.Skills.Controllers.Admin;
using Tnzi.Security.Authorization;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// 用户端技能控制器的可达性契约：<b>没有任何端点是匿名可达的</b>。
/// </summary>
/// <remarks>
/// 三个读端点（列表 / 详情 / 搜索）曾带 <c>[AllowAnonymous]</c>。它们返回的是技能的完整
/// 提示词正文，搜索端点还会在关键词命中不足时对查询串现算一次嵌入（计费调用）。
/// 三个端点的匿名性此前<b>零测试覆盖</b>，于是"锁写放读"这个意图与"任何人都能读走全部
/// System/Tenant 技能正文并驱动嵌入账单"这个结果，在代码里长得一模一样。
/// </remarks>
public class SkillControllerAuthorizationTests
{
    [Fact]
    public void UserSkillController_RequiresAuthentication_AtClassLevel()
    {
        var authorize = typeof(DefaultSkillController)
            .GetCustomAttributes<ApiAuthorizeAttribute>(inherit: true)
            .ToList();

        authorize.ShouldNotBeEmpty();
    }

    [Fact]
    public void UserSkillController_HasNoAnonymousEndpoint()
    {
        var anonymous = PublicActions(typeof(DefaultSkillController))
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null)
            .Select(m => m.Name)
            .ToList();

        anonymous.ShouldBeEmpty(
            $"These skill endpoints are anonymously reachable: {string.Join(", ", anonymous)}");
    }

    [Fact]
    public void UserSkillController_TypeItself_IsNotAnonymous()
    {
        typeof(DefaultSkillController)
            .GetCustomAttribute<AllowAnonymousAttribute>(inherit: true)
            .ShouldBeNull();
    }

    [Fact]
    public void AdminSkillController_HasNoAnonymousEndpoint()
    {
        var anonymous = PublicActions(typeof(DefaultSkillAdminController))
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null)
            .Select(m => m.Name)
            .ToList();

        anonymous.ShouldBeEmpty(
            $"These admin skill endpoints are anonymously reachable: {string.Join(", ", anonymous)}");
    }

    private static IEnumerable<MethodInfo> PublicActions(Type controller)
        => controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);
}
