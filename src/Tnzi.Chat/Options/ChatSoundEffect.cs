namespace Tnzi.Chat.Options;

/// <summary>
/// 聊天消息音效预设。前端用 WebAudio 实时合成对应音效（无二进制资源、无外部请求）。
/// 分两个家族：
/// <list type="bullet">
/// <item><b>Attention（通知型）</b>：较长、多音、引人注意——用于窗口关闭或非当前会话收到消息。</item>
/// <item><b>Subtle（对话型）</b>：短促、平和、低音量——用于当前会话内收发消息，仅作体验反馈。</item>
/// </list>
/// <c>None</c> = 该类别静音。
/// </summary>
public enum ChatSoundEffect
{
    /// <summary>静音（该类别不播放）。</summary>
    None = 0,

    // ── Attention（通知型：较长、引人注意）────────────────────────────
    /// <summary>钟琴：两声下行铃音，温暖经典（默认通知音）。</summary>
    Chime = 1,
    /// <summary>叮咚：门铃式下行两声，熟悉的到达提示。</summary>
    DingDong = 2,
    /// <summary>三连音：三声上行琶音，明亮清脆。</summary>
    TriTone = 3,
    /// <summary>马林巴：三声木琴琶音，柔和活泼。</summary>
    Marimba = 4,
    /// <summary>脉冲：两声同音短促提示，醒目直接。</summary>
    Pulse = 5,
    /// <summary>铃：单声撞钟带长衰减尾音，优雅。</summary>
    Bell = 6,

    // ── Subtle（对话型：短促、平和）──────────────────────────────────
    /// <summary>气泡：单声柔和下滑气泡音（默认会话音）。</summary>
    Pop = 7,
    /// <summary>轻点：极短高频轻响，极简。</summary>
    Tick = 8,
    /// <summary>轻鸣：短促中频单音，中性。</summary>
    Blip = 9,
    /// <summary>柔和：略缓起音的低音量单音，平静。</summary>
    Soft = 10,
    /// <summary>水滴：短促上下滑音，悦耳。</summary>
    Drop = 11,
}
