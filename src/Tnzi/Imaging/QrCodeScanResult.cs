using System.Diagnostics.CodeAnalysis;

namespace Tnzi.Imaging;

/// <summary>
/// 一次 QR 读取的结果：读到了什么，或者什么都没读到。
/// </summary>
/// <remarks>
/// ★ <b>「没找到」用一个值表示，不用异常。</b>一张没有码的纸是正常输入而不是故障，
/// 用异常表达它会逼调用方把正常分支写进 catch 里。
/// </remarks>
/// <param name="Payload">读出来的载荷；没读到时为 <c>null</c>。</param>
public readonly record struct QrCodeScanResult(string? Payload)
{
    /// <summary>什么都没读到。</summary>
    public static QrCodeScanResult NotFound => default;

    /// <summary>读到了码。</summary>
    [MemberNotNullWhen(true, nameof(Payload))]
    public bool Found => Payload is not null;
}
