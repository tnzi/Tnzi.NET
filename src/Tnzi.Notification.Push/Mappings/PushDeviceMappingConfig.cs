using Tnzi.Mapping;

namespace Tnzi.Notification.Push.Mappings;

/// <summary>
/// <see cref="PushDevice"/> → <see cref="PushDeviceDto"/>：把令牌换成掩码。
/// </summary>
/// <remarks>
/// ★ <b>这条映射是掩码的唯一出口，别在服务层再抄一份。</b>令牌是否露出完整值,
/// 取决于「有没有人在某一处忘了掩码」—— 把它收在映射里，任何一条走
/// <c>MapTo&lt;PushDeviceDto&gt;</c> 的路径都自动带上，包括以后新增的查询端点。
/// 反过来，服务里手写一次 <c>new PushDeviceDto { ... }</c> 就绕开了它，而症状是
/// 一个返回全量令牌的接口看起来完全正常。
/// </remarks>
public class PushDeviceMappingConfig : IMappingConfig
{
    public void Configure(IMappingConfigContext context)
    {
        context.NewConfig<PushDevice, PushDeviceDto>()
            .Map(dest => dest.TokenMask, src => PushTokenMask.Of(src.Token));
    }
}
