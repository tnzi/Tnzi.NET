namespace Tnzi.Imaging;

/// <summary>
/// 一次 QR 码生成请求。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>决定这张码能不能被读回来的是「每个模块落在几个扫描像素上」，不是它有多大。</b>
/// 实测（模拟传真链路：600dpi 打印 → 光学模糊 → 204×98dpi 各向异性扫描 → 二值化 → 亚像素相位偏移 → 歪斜，
/// 每组 500 次）：2.93 像素/模块 100% 读回，3.33 像素/模块 98%，2.39 像素/模块只有 83%。
/// 接近整数的采样率稳定，接近 x.5 的最差 —— <b>「印大一点」并不单调地更可靠</b>。
/// 所以本类同时给出 <see cref="TargetSize"/>（方便）与 <see cref="PixelsPerModule"/>（准确），
/// 知道自己在算什么的调用方应当用后者。
/// </para>
/// <para>
/// ★ <b>提高纠错等级换不回采样精度。</b>同一物理尺寸下把等级从 Q 提到 H，实测反而从 100% 掉到 82% ——
/// 因为 H 把一个 14 字符的载荷从版本 1 顶到了版本 2，模块随之变小。要控制的是版本，不是纠错等级，
/// 这正是 <see cref="MaxVersion"/> 存在的理由。
/// </para>
/// </remarks>
public class QrCodeRequest
{
    /// <summary>默认每模块像素数：既不设 <see cref="PixelsPerModule"/> 也不设 <see cref="TargetSize"/> 时用它。</summary>
    public const int DefaultPixelsPerModule = 8;

    /// <summary>QR 规范要求的最小静区宽度（模块数）。</summary>
    public const int MinimumQuietZoneModules = 4;

    /// <summary>
    /// 要编码的文本。
    /// </summary>
    /// <remarks>
    /// 载荷越短版本越低、模块越大、越读得回来。要落在版本 1 的话：纯数字 ≤ 17 位、
    /// 大写字母与数字 ≤ 16 位（纠错等级 Q）。
    /// </remarks>
    public string Payload { get; set; } = null!;

    /// <summary>
    /// 最低纠错等级，默认 <see cref="QrErrorCorrection.Medium"/>。
    /// </summary>
    /// <remarks>
    /// 是「最低」而非「就用这个」：<see cref="BoostErrorCorrection"/> 打开时，
    /// 只要不需要更大的版本就会自动提高。实际用上的等级见 <see cref="QrCodeImage.ErrorCorrection"/>。
    /// </remarks>
    public QrErrorCorrection ErrorCorrection { get; set; } = QrErrorCorrection.Medium;

    /// <summary>
    /// 在不增大版本的前提下自动提高纠错等级，默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// 提级不会改变模块数（这是它的定义），所以在物理尺寸不变的前提下只多不少 ——
    /// 与「手动把等级提到 H」是两回事，后者会顶高版本。
    /// </remarks>
    public bool BoostErrorCorrection { get; set; } = true;

    /// <summary>
    /// 每个模块占几个像素；<c>0</c> 表示由 <see cref="TargetSize"/> 推算。
    /// </summary>
    /// <remarks>
    /// 设了它就以它为准，<see cref="TargetSize"/> 被忽略。这是唯一能让产出的模块边缘落在整像素上的写法。
    /// </remarks>
    public int PixelsPerModule { get; set; }

    /// <summary>
    /// 期望的输出边长（像素）；<c>0</c> 表示不限。
    /// </summary>
    /// <remarks>
    /// ★ <b>是上界不是等号。</b>实现会取「装得下的最大整数每模块像素数」，因此实际边长通常略小于此值 ——
    /// 真值在 <see cref="QrCodeImage.Width"/>。不这么做的话模块宽度会一个 3 像素一个 4 像素地参差，
    /// 而扫描端正是靠模块边界对齐来定位的。
    /// </remarks>
    public int TargetSize { get; set; }

    /// <summary>
    /// 静区（四周留白）宽度，单位是模块，默认 <see cref="MinimumQuietZoneModules"/>。
    /// </summary>
    /// <remarks>
    /// 不要调低。静区是解码器判定码边界的依据，没有静区的码在现实纸张上基本读不出来。
    /// </remarks>
    public int QuietZoneModules { get; set; } = MinimumQuietZoneModules;

    /// <summary>
    /// 允许的最小版本（1-40）；<c>0</c> 表示不限。
    /// </summary>
    /// <remarks>
    /// 用途是<b>让模块数不随载荷长度浮动</b>：把它和 <see cref="MaxVersion"/> 设成同一个值，
    /// 每一份产出的码就都是同样的格数，版面上那个方框的尺寸因此可以写死。
    /// </remarks>
    public int MinVersion { get; set; }

    /// <summary>
    /// 允许的最大版本（1-40）；<c>0</c> 表示不限。
    /// </summary>
    /// <remarks>
    /// ★ 载荷放不下时<b>抛异常而不是升版本</b>。这是刻意的：升版本会让模块变小，
    /// 而排版早就按某个模块尺寸定好了 —— 静默升级的表现不是报错，是几个月后发现有一批纸读不回来。
    /// </remarks>
    public int MaxVersion { get; set; }
}

/// <summary>
/// QR 码纠错等级。
/// </summary>
/// <remarks>括号里是可恢复的码字比例（QR 规范定义）。等级越高越抗污损，但同样的载荷需要更多模块。</remarks>
public enum QrErrorCorrection
{
    /// <summary>L：约 7%。</summary>
    Low = 0,

    /// <summary>M：约 15%。QR 码事实上的默认等级。</summary>
    Medium = 1,

    /// <summary>Q：约 25%。纸质回传（打印—签署—扫描/传真）实测的甜点。</summary>
    Quartile = 2,

    /// <summary>H：约 30%。★ 别为了「更保险」直接选它 —— 见 <see cref="QrCodeRequest"/> 的说明。</summary>
    High = 3
}
