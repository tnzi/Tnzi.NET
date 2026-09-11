using System.Security.Cryptography;
using System.Text;

namespace Tnzi.Notification.Push.Services.Internal;

/// <summary>
/// 匿名设备的 <c>Id</c> 由它的设备密钥派生而来。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么不是随机生成的。</b>这个 Id 是消费方写进业务记录的设备归属键
/// （哪台设备提交了这份表单），而推送令牌随时可能被网关判死、连带整行被退役删掉。
/// 若 Id 是随机的，客户端带着同一枚密钥回来时只能拿到一个<b>新的</b> Id，
/// 此前所有表单的回执就永久断了 —— 而且毫无症状：表单还在、状态正常，只是再没有推送来过。
/// 派生让重建出的 Id 与当初签发的<b>逐字相同</b>。
/// </para>
/// <para>
/// ★ <b>这同时是一道认证。</b>行已经不在时，服务端无从查证客户端声称的那个 Id 是不是它的：
/// 任何人都能报一个别人的 deviceId。而派生关系是可验证的 —— 只有持有密钥的一方
/// 才算得出这个 Id，所以服务端<b>不接受客户端上报的 Id</b>，一律自己从密钥算。
/// </para>
/// <para>
/// ★ 取哈希前 16 字节，<b>不可逆</b>：Id 会出现在业务记录、管理界面和日志里，
/// 它泄露不能反推出密钥。碰撞概率是 128 位空间上的生日问题，可忽略。
/// </para>
/// </remarks>
internal static class AnonymousDeviceId
{
    /// <summary>
    /// 从设备密钥算出这台设备的 Id。同一枚密钥恒得同一个 Id。
    /// </summary>
    /// <param name="deviceKey">签发给客户端的设备密钥明文。</param>
    /// <exception cref="ArgumentException">密钥为 null 或空白。</exception>
    internal static Guid From(string deviceKey)
    {
        Check.NotNullOrWhiteSpace(deviceKey);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(deviceKey), digest);

        // bigEndian: true 让字节顺序就是摘要本身的顺序，与运行平台无关。
        // 默认那个重载按 Guid 的内存布局解释前几段，结果虽然在同一平台上稳定，
        // 却把「这个 Id 怎么来的」变成一件依赖实现细节的事 —— 而这个值要长期
        // 存在消费方的业务表里，跨进程、跨版本、可能还跨架构地被重新算一遍。
        return new Guid(digest[..16], bigEndian: true);
    }
}
