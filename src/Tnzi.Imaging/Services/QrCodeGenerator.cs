// Net.Codecrete 的类型刻意只以别名引入：它的根命名空间就叫 QrCodeGenerator，
// 与本类同名，直接 using 会让「QrCodeGenerator」在本文件里指代不明。
using DataTooLong = Net.Codecrete.QrCodeGenerator.DataTooLongException;
using QrEcc = Net.Codecrete.QrCodeGenerator.QrCode.Ecc;
using QrEncoder = Net.Codecrete.QrCodeGenerator.QrCode;

namespace Tnzi.Imaging.Services;

/// <inheritdoc cref="IQrCodeGenerator"/>
/// <remarks>
/// 基于 <c>Net.Codecrete.QrCodeGenerator</c>（MIT，纯托管、零传递依赖，自带 PNG 写出）。
/// <para>
/// ★ <b>没有选 QRCoder</b>：它在 <c>net6.0</c> 这一档带着 <c>System.Drawing.Common</c> 依赖，
/// 而 .NET 10 会解析到那一档。我们只会用它不碰 System.Drawing 的那条路径，
/// 但依赖闭包不看你调不调用 —— 每个消费应用的发布产物里都会多出一个仅限 Windows 的过时包。
/// </para>
/// </remarks>
public class QrCodeGenerator : IQrCodeGenerator
{
    /// <summary>静区上限（模块）。超过这个宽度的留白只是在浪费像素。</summary>
    private const int MaxQuietZoneModules = 16;

    /// <summary>每模块像素数上限。</summary>
    private const int MaxPixelsPerModule = 64;

    /// <inheritdoc />
    public QrCodeImage Generate(QrCodeRequest request)
    {
        Check.NotNull(request);
        Check.NotNullOrWhiteSpace(request.Payload);
        Validate(request);

        var minVersion = request.MinVersion > 0 ? request.MinVersion : QrEncoder.MinVersion;
        var maxVersion = request.MaxVersion > 0 ? request.MaxVersion : QrEncoder.MaxVersion;

        QrEncoder code;
        try
        {
            code = QrEncoder.EncodeTextAdvanced(
                request.Payload,
                ToEcc(request.ErrorCorrection),
                boostEcl: request.BoostErrorCorrection,
                minVersion: minVersion,
                maxVersion: maxVersion);
        }
        catch (DataTooLong ex)
        {
            // 刻意翻译成 ArgumentException 而不是放它自己冒出去：调用方不该为了处理
            // 「码放不下」去引用一个第三方库的异常类型。
            throw new ArgumentException(
                $"The payload ({request.Payload.Length} characters) does not fit in a QR code of version {maxVersion} "
                + $"at error correction level {request.ErrorCorrection}. Shorten the payload or raise MaxVersion.",
                nameof(request),
                ex);
        }

        var totalModules = code.Size + (2 * request.QuietZoneModules);
        var pixelsPerModule = ResolvePixelsPerModule(request, totalModules);

        var png = code.ToPngBitmap(scale: pixelsPerModule, border: request.QuietZoneModules);

        return new QrCodeImage(
            png,
            code.Version,
            pixelsPerModule,
            request.QuietZoneModules,
            FromEcc(code.ErrorCorrectionLevel));
    }

    /// <summary>
    /// 取「装得下的最大整数每模块像素数」。
    /// </summary>
    /// <remarks>
    /// ★ 刻意向下取整而不是拉伸到正好 <see cref="QrCodeRequest.TargetSize"/>：
    /// 非整数倍会让模块宽度在 3 和 4 像素之间参差，而扫描端正是靠模块边界的规则性定位的。
    /// 少几个像素换来边缘干净，这个交换在纸质回路上是划算的。
    /// </remarks>
    private static int ResolvePixelsPerModule(QrCodeRequest request, int totalModules)
    {
        if (request.PixelsPerModule > 0)
        {
            return request.PixelsPerModule;
        }

        if (request.TargetSize > 0)
        {
            return Math.Max(1, request.TargetSize / totalModules);
        }

        return QrCodeRequest.DefaultPixelsPerModule;
    }

    private static void Validate(QrCodeRequest request)
    {
        if (request.QuietZoneModules < 0 || request.QuietZoneModules > MaxQuietZoneModules)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), $"QuietZoneModules must be between 0 and {MaxQuietZoneModules}.");
        }

        if (request.PixelsPerModule < 0 || request.PixelsPerModule > MaxPixelsPerModule)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), $"PixelsPerModule must be between 0 and {MaxPixelsPerModule}.");
        }

        if (request.TargetSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "TargetSize cannot be negative.");
        }

        ValidateVersion(request.MinVersion, nameof(QrCodeRequest.MinVersion));
        ValidateVersion(request.MaxVersion, nameof(QrCodeRequest.MaxVersion));

        if (request.MinVersion > 0 && request.MaxVersion > 0 && request.MinVersion > request.MaxVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), "MinVersion cannot be greater than MaxVersion.");
        }
    }

    private static void ValidateVersion(int version, string name)
    {
        if (version != 0 && (version < QrEncoder.MinVersion || version > QrEncoder.MaxVersion))
        {
            throw new ArgumentOutOfRangeException(
                nameof(QrCodeRequest),
                $"{name} must be 0 (unconstrained) or between {QrEncoder.MinVersion} and {QrEncoder.MaxVersion}.");
        }
    }

    // 两个方向都逐项写出而不是强制转换：序数一致是当下的巧合，不是契约。
    // 哪天上游调整了顺序，强制转换会安静地把 Q 变成 H —— 而那恰好是实测里让读回率掉两成的那一步。
    private static QrEcc ToEcc(QrErrorCorrection level) => level switch
    {
        QrErrorCorrection.Low => QrEcc.Low,
        QrErrorCorrection.Medium => QrEcc.Medium,
        QrErrorCorrection.Quartile => QrEcc.Quartile,
        QrErrorCorrection.High => QrEcc.High,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown error correction level.")
    };

    private static QrErrorCorrection FromEcc(QrEcc level) => level switch
    {
        QrEcc.Low => QrErrorCorrection.Low,
        QrEcc.Medium => QrErrorCorrection.Medium,
        QrEcc.Quartile => QrErrorCorrection.Quartile,
        QrEcc.High => QrErrorCorrection.High,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown error correction level.")
    };
}
