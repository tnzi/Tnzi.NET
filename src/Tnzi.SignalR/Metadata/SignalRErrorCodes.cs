namespace Tnzi.SignalR.Metadata;

/// <summary>
/// SignalR 模块的业务错误码。
/// </summary>
public static class SignalRErrorCodes
{
    /// <summary>
    /// 实时连接管理端只对宿主开放，被钉在某个租户上的调用者答 403（见 <see cref="Filters.HostContextOnlyFilter"/>）。
    /// </summary>
    public const string HostManaged = "SIGNALR_HOST_MANAGED";
}
