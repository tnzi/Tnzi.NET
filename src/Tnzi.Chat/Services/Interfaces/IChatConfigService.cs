namespace Tnzi.Chat.Services.Interfaces;

/// <summary>
/// 聊天客户端配置服务：把 <see cref="ChatOptions"/> 中与前端相关的开关
/// 投影为 <see cref="ChatClientConfigDto"/>，供消费应用按部署配置裁剪聊天 UI。
/// </summary>
public interface IChatConfigService
{
    /// <summary>获取客户端功能配置（前端据此显隐入口；写路径仍由服务端强制）。</summary>
    Result<ChatClientConfigDto> GetClientConfig();
}
