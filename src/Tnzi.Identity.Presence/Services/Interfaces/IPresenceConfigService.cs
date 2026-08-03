namespace Tnzi.Identity.Presence.Services;

public interface IPresenceConfigService
{
    /// <summary>投影 presence 客户端配置（供前端读取 auto-away 阈值、隐身开关等）。</summary>
    Task<Result<PresenceClientConfigDto>> GetClientConfigAsync();
}
