namespace Tnzi.Chat.Entities;

/// <summary>用户在线状态。1-4 为手动可选；Offline 为系统解析值（无连接/隐身对外）。</summary>
public enum UserPresenceStatus
{
    Online = 1,
    Away = 2,
    Busy = 3,
    Invisible = 4,
    Offline = 5,
}
