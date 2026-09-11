using Tnzi.Notification.Push.Services.Internal;

namespace Tnzi.Notification.Push.Tests;

/// <summary>
/// 匿名设备密钥的形态契约。
/// </summary>
/// <remarks>
/// 刷新路径必须接受一枚<b>查不到对应行</b>的密钥（行被退役后要能重建），所以「这是不是本服务
/// 签发的」在库里查不出来 —— 形态校验是唯一挡得住「客户端图省事，直接填一个现成的公开标识」
/// 的地方。而那正是这条设计要防的东西：那样一来密钥就成了会流进业务表、日志与支持工单的公开值。
/// </remarks>
public class AnonymousDeviceKeyTests
{
    /// <summary>
    /// ★ 声明的长度必须与真实签发结果一致。
    /// </summary>
    /// <remarks>
    /// 这个常量是从 <c>OneTimeToken.DefaultEntropyBytes</c> 手算出来的。哪天那个默认熵改了，
    /// 校验就会开始<b>拒掉本服务自己刚签发的密钥</b> —— 症状是所有匿名客户端在第二次启动时
    /// 突然全部注册失败，而签发那一次是成功的。对着真实签发结果断言，让那次改动当场变红。
    /// </remarks>
    [Fact]
    public void The_declared_length_matches_what_issuing_actually_produces()
        => AnonymousDeviceKey.Issue().Length.ShouldBe(AnonymousDeviceKey.Length);

    /// <summary>签发出来的密钥必须能通过自己的形态校验。</summary>
    [Fact]
    public void An_issued_key_passes_its_own_shape_check()
        => AnonymousDeviceKey.IsWellFormed(AnonymousDeviceKey.Issue()).ShouldBeTrue();

    /// <summary>
    /// ★★ 挡住的正是「顺手拿一个现成的公开标识来用」的那些形态。
    /// </summary>
    /// <remarks>
    /// 这些值都不是假想的：<c>identifierForVendor</c>、Android SSAID、邮箱、递增数字，
    /// 是客户端为了省掉「存一个密钥」那一行时最可能填进去的东西。
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("3F2504E0-4F89-11D3-9A0C-0305E82C3301")]   // iOS identifierForVendor / 任意 UUID
    [InlineData("9774d56d682e549c")]                        // Android SSAID
    [InlineData("user@example.com")]
    [InlineData("12345")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa=")]  // 43 位但带 base64 填充符
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa+")]  // 43 位但是标准 base64 字符集
    public void Values_that_were_not_issued_by_us_are_refused(string? candidate)
        => AnonymousDeviceKey.IsWellFormed(candidate).ShouldBeFalse();

    /// <summary>
    /// ★ 这道校验<b>不保证熵</b>，只提高下限 —— 一个刻意构造的 43 位低熵串照样通得过。
    /// </summary>
    /// <remarks>
    /// 断言它通过而不是失败，是为了让这条限制在测试里读得出来：与口令复杂度规则一样，
    /// 它挡的是「顺手用了现成的值」，挡不住明知故犯。把它误当成熵的保证，
    /// 下一个人就会在别处省掉真正需要的那道防御。
    /// </remarks>
    [Fact]
    public void The_shape_check_bounds_the_format_not_the_entropy()
        => AnonymousDeviceKey.IsWellFormed(new string('a', AnonymousDeviceKey.Length)).ShouldBeTrue();
}
