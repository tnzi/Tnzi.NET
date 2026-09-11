namespace Tnzi.Notification.Push.Metadata;

/// <summary>
/// 一台已注册设备所在的平台。
/// </summary>
/// <remarks>
/// ★ <b>它不决定投递走哪条路。</b>三个平台的推送都经 FCM 发出（iOS 由 FCM 转投 APNs），
/// 所以这个值只用于运维辨认与统计 —— 「这个用户在哪几种端上装了」。把它当成投递分支的
/// 依据会引入一条框架并不支持的路径：本模块只有 FCM 一个真实 provider。
/// <para>
/// 住在 <c>Metadata/</c> 而不是与实体同目录：它出现在消费方看得见的 DTO 上，是公开契约，
/// 按 docs/coding-standards/metadata.md 的分界属于「共享/公开」那一类。
/// </para>
/// </remarks>
public enum DevicePlatform
{
    /// <summary>Android 设备。</summary>
    Android = 1,

    /// <summary>iOS 设备（推送仍经 FCM 转投 APNs）。</summary>
    Ios = 2,

    /// <summary>浏览器（Web Push，令牌由 Firebase JS SDK 取得）。</summary>
    Web = 3
}
