namespace Tnzi.Finance.Documents.Services.Internal;

/// <summary>
/// 支票套打校准页（毫米刻度标尺 + 版式分区示意）的 HTML 生成
/// </summary>
/// <remarks>
/// 打在预印票纸上与实际票面比对，据此调 <c>BankAccount.OffsetXMm/OffsetYMm</c>。
/// 校准页是诊断产物而非业务单据，故不入模板库（不需要被管理端编辑），在代码里生成。
/// 尺度与 <c>check-cpa006-ca</c> 模板保持一致：支票本体 88.9mm，MICR 净空带 15.9mm。
/// 内联 CSS 用 <c>$$"""</c> 原始插值串（双花括号才是插值洞，CSS 的单花括号原样输出）。
/// </remarks>
internal static class CheckCalibrationSheet
{
    private const decimal PageWidthMm = 215.9m;
    private const decimal PageHeightMm = 279.4m;
    private const decimal ChequeHeightMm = 88.9m;
    private const decimal MicrBandHeightMm = 15.9m;

    /// <summary>刻度间隔（毫米）</summary>
    private const int TickStepMm = 10;

    public static string Build(CheckRenderRequest request)
    {
        Check.NotNull(request);

        var offsetStyle = request.OffsetXMm == 0m && request.OffsetYMm == 0m
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"transform: translate({request.OffsetXMm}mm, {request.OffsetYMm}mm);");
        var micrBandTopMm = ChequeHeightMm - MicrBandHeightMm;

        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8" />
            <title>Cheque alignment calibration</title>
            <style>
            @page { size: auto; margin: 0; }
            * { margin: 0; padding: 0; box-sizing: border-box; }
            body { background: #eef0f3; font-family: Arial, Helvetica, sans-serif; color: #000; }
            .sheet { position: relative; width: {{PageWidthMm}}mm; height: {{PageHeightMm}}mm; margin: 0 auto; background: #fff; overflow: hidden; }
            .tick { position: absolute; background: #b9bec6; }
            .tick-h { left: 0; width: {{PageWidthMm}}mm; height: 0.2mm; }
            .tick-v { top: 0; height: {{PageHeightMm}}mm; width: 0.2mm; }
            .tick-label { position: absolute; font-size: 5pt; color: #6b7280; }
            .zone { position: absolute; left: 0; width: {{PageWidthMm}}mm; border-bottom: 0.4mm solid #d33; }
            .zone-label { position: absolute; font-size: 7pt; color: #d33; font-weight: 700; }
            .info { position: absolute; left: 12mm; top: 24mm; width: 130mm; font-size: 9pt; line-height: 1.6; }
            .info h1 { font-size: 12pt; margin-bottom: 2mm; }
            @media print { body { background: #fff; } .sheet { margin: 0; } }
            </style>
            </head>
            <body>
            <div class="sheet" style="{{offsetStyle}}">
            """));

        for (var mm = 0; mm <= (int)PageHeightMm; mm += TickStepMm)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"""<div class="tick tick-h" style="top:{mm}mm"></div><div class="tick-label" style="left:1mm; top:{mm}mm">{mm}</div>"""));
        }

        for (var mm = 0; mm <= (int)PageWidthMm; mm += TickStepMm)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"""<div class="tick tick-v" style="left:{mm}mm"></div><div class="tick-label" style="left:{mm}mm; top:1mm">{mm}</div>"""));
        }

        AppendZone(sb, ChequeHeightMm, "cheque body ends");
        AppendZone(sb, micrBandTopMm, "MICR clear band starts");

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""
            <div class="info">
              <h1>Cheque alignment calibration</h1>
              <div>Bank account: {Escape(request.AccountName)}</div>
              <div>Layout: {request.Layout} &middot; Stock: {request.StockType}</div>
              <div>Offset X: {request.OffsetXMm}mm &middot; Offset Y: {request.OffsetYMm}mm</div>
              <div>Template: {Escape(request.TemplateName ?? CheckTemplates.DefaultName)}</div>
              <div>Cheque body ends at {ChequeHeightMm}mm, MICR clear band starts at {micrBandTopMm}mm.</div>
              <div>Print at 100% scale (disable "fit to page"), lay the sheet over the pre-printed
                  stock and adjust the bank account offsets by the measured difference.</div>
            </div>
            </div>
            </body>
            </html>
            """));

        return sb.ToString();
    }

    private static void AppendZone(StringBuilder sb, decimal topMm, string label)
    {
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"""<div class="zone" style="top:{topMm}mm"></div><div class="zone-label" style="left:150mm; top:{topMm - 4m}mm">{label} ({topMm}mm)</div>"""));
    }

    private static string Escape(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : System.Net.WebUtility.HtmlEncode(value);
}
