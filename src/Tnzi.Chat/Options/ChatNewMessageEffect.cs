namespace Tnzi.Chat.Options;

/// <summary>
/// 新消息且聊天窗口关闭时，启动器图标（header 聊天入口）的视觉提醒动效。
/// 借鉴主流 IM 的"引起注意"手法（微信/QQ 桌面端图标晃动、macOS Dock 弹跳、MSN 闪烁）。
/// 纯 CSS 动画，短暂播放一次；<c>None</c> = 不做动效（仍保留未读徽标）。
/// </summary>
public enum ChatNewMessageEffect
{
    /// <summary>不做动效（仅未读徽标）。</summary>
    None = 0,

    /// <summary>晃动：图标左右摇摆（默认，微信/QQ 桌面端手法）。</summary>
    Shake = 1,

    /// <summary>脉冲：图标缩放一次并带光环扩散。</summary>
    Pulse = 2,

    /// <summary>闪烁：图标短暂闪烁并高亮主题色（经典 MSN/QQ 手法）。</summary>
    Blink = 3,

    /// <summary>弹跳：图标上下弹跳（macOS Dock 手法）。</summary>
    Bounce = 4,
}
