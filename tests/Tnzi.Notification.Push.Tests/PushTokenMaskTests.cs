
namespace Tnzi.Notification.Push.Tests;

/// <summary>
/// 令牌对外展示时的掩码。
/// </summary>
/// <remarks>
/// ★★ 令牌<b>是凭据</b>：注册表按令牌寻址，持有它的人可以改写或掐掉那台设备的推送归属。
/// 掩码同时还保护<b>那张清单</b>：一次列表查询就能导出「谁在哪几台设备上装了这个 App」。
/// </remarks>
public class PushTokenMaskTests
{
    /// <summary>正常长度的令牌只露出尾部若干位。</summary>
    [Fact]
    public void A_normal_token_keeps_only_its_tail()
    {
        const string token = "fXYZ123abcdefghijklmnopqrstuvwxyz0123456789TAILPART";

        var mask = PushTokenMask.Of(token);

        mask.ShouldNotContain(token);
        mask.ShouldEndWith(token[^PushTokenMask.VisibleTailLength..]);
        mask.Length.ShouldBeLessThan(token.Length);
    }

    /// <summary>
    /// ★ 短到不足以掩码的值<b>整个隐藏</b>，绝不原样返回。
    /// </summary>
    /// <remarks>
    /// 一个「太短所以直接给你看」的分支，会在遇到非预期格式的令牌（截断的、
    /// 测试桩写死的、别家 provider 的短令牌）时安静地把它完整露出来 ——
    /// 而那正是掩码存在的场景。这条用例锁住「没有这个分支」。
    /// </remarks>
    [Theory]
    [InlineData("a")]
    [InlineData("abcdefg")]
    [InlineData("abcdefgh")]
    public void A_token_too_short_to_mask_is_hidden_entirely_rather_than_returned_raw(string token)
    {
        var mask = PushTokenMask.Of(token);

        mask.ShouldNotContain(token);
    }

    /// <summary>空值不炸，也不产生一个看着像掩码的空壳。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_token_masks_to_empty(string? token)
    {
        PushTokenMask.Of(token).ShouldBeEmpty();
    }
}
