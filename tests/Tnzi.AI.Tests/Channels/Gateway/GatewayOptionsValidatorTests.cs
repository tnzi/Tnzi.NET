using Tnzi.AI.Channels.Options;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// 网关路径必须以 <c>/</c> 开头：它要与请求路径逐字比较，并登记进查询串令牌的路径白名单（按路径段匹配），
/// 写成 <c>ws/gateway</c> 时端点永远匹配不上任何请求，浏览器客户端也永远认证不了，而启动一切正常。
/// </summary>
public class GatewayOptionsValidatorTests
{
    [Theory]
    [InlineData("/ws/gateway", true)]
    [InlineData("/realtime/ai", true)]
    [InlineData("ws/gateway", false)]
    [InlineData("", false)]
    public void Path_MustStartWithASlash(string path, bool valid)
    {
        var result = new GatewayOptionsValidator().Validate(null, new GatewayOptions { Path = path });

        result.Succeeded.ShouldBe(valid);
    }
}
