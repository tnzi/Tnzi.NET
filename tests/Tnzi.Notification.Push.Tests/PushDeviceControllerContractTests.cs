using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Tnzi.Notification.Push.Controllers;
using Tnzi.Security.Authorization;

namespace Tnzi.Notification.Push.Tests;

/// <summary>
/// 用户端控制器上「哪些端点匿名可达」这条契约。
/// </summary>
/// <remarks>
/// <para>
/// 类级挂着 <c>[ApiAuthorize]</c>，两个匿名端点靠方法级 <c>[AllowAnonymous]</c> 逐个豁免
/// （<c>ApiAuthorizeAttribute</c> 继承自 <c>AuthorizeAttribute</c>，走标准授权管线，
/// 所以方法级豁免是生效的）。
/// </para>
/// <para>
/// ★ <b>两个方向都要断言。</b>删掉某个 <c>[AllowAnonymous]</c> 不会有编译错误、
/// 别的测试也不会红，而线上表现是没有账号的客户端在注册时拿到 401 —— 对一个 App 来说
/// 那与网络故障长得一模一样，它只会一直重试。反过来，给别的端点误加一个
/// <c>[AllowAnonymous]</c>，就是把某个用户的设备清单变成公开数据。
/// </para>
/// </remarks>
public class PushDeviceControllerContractTests
{
    private static MethodInfo Method(string name)
        => typeof(DefaultPushDeviceController).GetMethod(name)
           ?? throw new InvalidOperationException($"No public method named {name}.");

    /// <summary>类级门禁还在：三个匿名端点是<b>豁免</b>，不是「这个控制器本来就不设防」。</summary>
    [Fact]
    public void The_controller_still_carries_a_class_level_authorization_gate()
        => typeof(DefaultPushDeviceController)
            .GetCustomAttributes<ApiAuthorizeAttribute>(inherit: true)
            .ShouldNotBeEmpty();

    [Theory]
    [InlineData(nameof(DefaultPushDeviceController.RegisterAnonymous))]
    [InlineData(nameof(DefaultPushDeviceController.UnregisterAnonymous))]
    public void Anonymous_endpoints_opt_out_of_the_class_level_gate(string method)
        => Method(method).GetCustomAttribute<AllowAnonymousAttribute>().ShouldNotBeNull();

    [Theory]
    [InlineData(nameof(DefaultPushDeviceController.Register))]
    [InlineData(nameof(DefaultPushDeviceController.GetMine))]
    [InlineData(nameof(DefaultPushDeviceController.Unregister))]
    [InlineData(nameof(DefaultPushDeviceController.Remove))]
    public void Signed_in_endpoints_stay_behind_the_class_level_gate(string method)
        => Method(method).GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();

    /// <summary>
    /// 端点都是 <c>virtual</c> 的，消费方要覆写其中一个（典型是给匿名那三个加限流特性）时
    /// 不必整个控制器重写一遍。
    /// </summary>
    [Theory]
    [InlineData(nameof(DefaultPushDeviceController.RegisterAnonymous))]
    [InlineData(nameof(DefaultPushDeviceController.UnregisterAnonymous))]
    public void Anonymous_endpoints_are_virtual_so_a_host_can_attach_its_own_rate_limiting(string method)
        => Method(method).IsVirtual.ShouldBeTrue();
}
