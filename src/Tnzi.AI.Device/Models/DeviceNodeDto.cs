namespace Tnzi.AI.Device.Models;

/// <summary>
/// 设备节点 DTO
/// </summary>
public class DeviceNodeDto
{
    /// <summary>节点 ID</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>设备名称</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>设备平台</summary>
    public DevicePlatform Platform { get; init; }

    /// <summary>连接状态</summary>
    public DeviceConnectionState State { get; init; }

    /// <summary>能力列表（capability family 名称）</summary>
    public List<string> Capabilities { get; init; } = new();
}
