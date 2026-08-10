using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Formats.Png;

namespace Tnzi.Imaging.Services;

/// <summary>
/// 滑动验证码服务实现
/// </summary>
public class SlidingCaptchaService : ApplicationService, ISlidingCaptchaService
{
    private readonly ICache? _cache;
    private readonly IOptions<ImagingOptions> _imagingOptions;

    private const string CacheKeyPrefix = "SlidingCaptcha:";
    private const string FailureCacheKeyPrefix = "captcha:failures:";

    /// <summary>
    /// 失败计数的保留窗口（分钟）：超过这个时间没有新的失败即视为重新开始
    /// </summary>
    private const int FailureWindowMinutes = 30;

    public SlidingCaptchaService(
        IServiceProvider serviceProvider,
        IOptions<ImagingOptions> imagingOptions,
        ICache? cache = null) : base(serviceProvider)
    {
        _imagingOptions = Check.NotNull(imagingOptions);
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<Result<SlidingCaptchaDto>> GenerateAsync(SlidingCaptchaOptions? options = null, CancellationToken cancellationToken = default)
    {
        var opts = options ?? _imagingOptions.Value.SlidingCaptcha;
        return await GeneratePuzzleAsync(opts, addNoise: false, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<SlidingCaptchaVerifyResult>> VerifyAsync(string token, int userX, int tolerance = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
        {
            return Ok(new SlidingCaptchaVerifyResult { Success = false, Message = "Token is required" });
        }

        if (_cache == null)
        {
            return Fail<SlidingCaptchaVerifyResult>("Cache service is not available");
        }

        var cacheKey = GetCacheKey(token);
        var storedData = await _cache.GetAsync<SlidingCaptchaStoredData>(cacheKey, cancellationToken);

        if (storedData == null)
        {
            return Ok(new SlidingCaptchaVerifyResult { Success = false, Message = "Token expired or invalid" });
        }

        // 立即删除（一次性使用，防止 TOCTOU）
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        // 容差由生成时的服务端决策说话：自适应难度调紧的容差、以及配置里的
        // SlidingCaptcha.Tolerance 都随令牌一起存下，验证时优先采用；
        // 只有旧令牌（未带容差）才回退到入参。
        var effectiveTolerance = storedData.Tolerance > 0 ? storedData.Tolerance : tolerance;
        var diff = Math.Abs(userX - storedData.CorrectX);
        var isSuccess = diff <= effectiveTolerance;

        // 更新失败计数
        if (!string.IsNullOrEmpty(storedData.ClientId))
        {
            if (isSuccess)
            {
                // 验证成功，重置失败计数
                await ResetFailureCountAsync(storedData.ClientId, cancellationToken);
            }
            else
            {
                // 验证失败，增加失败计数
                await IncrementFailureCountAsync(storedData.ClientId, cancellationToken);
            }
        }

        return Ok(new SlidingCaptchaVerifyResult
        {
            Success = isSuccess,
            Message = isSuccess ? "Verification passed" : "Verification failed, please try again"
        });
    }

    /// <inheritdoc />
    public async Task<Result<SlidingCaptchaDto>> GenerateAdaptiveAsync(string? clientId = null, CancellationToken cancellationToken = default)
    {
        var baseOptions = _imagingOptions.Value.SlidingCaptcha;
        long failureCount = 0;

        if (!string.IsNullOrEmpty(clientId) && _cache != null)
        {
            failureCount = await GetFailureCountAsync(clientId, cancellationToken);
        }

        // 根据失败次数调整难度
        var (tolerance, pieceSize, addNoise) = GetDifficultySettings(failureCount);

        var adaptiveOptions = new SlidingCaptchaOptions
        {
            Width = baseOptions.Width,
            Height = baseOptions.Height,
            // 难度只会把拼图块变小（更难），不能超过配置的基线尺寸：
            // 基线是被 ImagingOptionsValidator 校验过与 Width/Height 相容的那个值
            PieceSize = Math.Min(baseOptions.PieceSize, pieceSize),
            Tolerance = tolerance,
            ExpirationMinutes = baseOptions.ExpirationMinutes
        };

        return await GeneratePuzzleAsync(adaptiveOptions, addNoise, clientId, cancellationToken);
    }

    /// <summary>
    /// 根据失败次数获取难度设置
    /// </summary>
    private static (int Tolerance, int PieceSize, bool AddNoise) GetDifficultySettings(long failureCount)
    {
        return failureCount switch
        {
            <= 2 => (5, 50, false),   // 普通难度
            <= 5 => (3, 40, false),   // 较高难度
            _ => (2, 35, true)        // 最高难度
        };
    }

    /// <summary>
    /// 生成拼图验证码核心逻辑
    /// </summary>
    private async Task<Result<SlidingCaptchaDto>> GeneratePuzzleAsync(
        SlidingCaptchaOptions options,
        bool addNoise,
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var width = options.Width;
        var height = options.Height;
        var pieceSize = options.PieceSize;
        var random = Random.Shared;

        // 随机拼图位置（确保拼图块不超出边界，且 X 有足够滑动空间）
        var minX = pieceSize + 10;
        var maxX = width - pieceSize - 10;
        var correctX = random.Next(minX, maxX);
        var pieceY = random.Next(10, height - pieceSize - 10);

        // 生成拼图路径
        var puzzlePath = CreateJigsawPath(correctX, pieceY, pieceSize);

        // 背景与拼图块必须画出逐像素相同的底图，否则拼图块和缺口对不上、验证码不可解。
        // 两次渲染各自用同一个种子新建 Random：共享一个 Random 实例会因序列已被第一次
        // 渲染推进而画出完全不同的渐变和装饰元素。
        var backgroundSeed = random.Next();

        // 生成背景图片
        var backgroundBase64 = GenerateBackgroundImage(width, height, puzzlePath, addNoise, new Random(backgroundSeed));

        // 生成拼图块图片
        var puzzlePieceBase64 = GeneratePuzzlePiece(width, height, puzzlePath, new Random(backgroundSeed));

        // 生成令牌并存储
        var token = Guid.NewGuid().ToString("N");

        if (_cache != null)
        {
            var storedData = new SlidingCaptchaStoredData
            {
                CorrectX = correctX,
                Tolerance = options.Tolerance,
                ClientId = clientId
            };
            var cacheKey = GetCacheKey(token);
            await _cache.SetAsync(cacheKey, storedData, TimeSpan.FromMinutes(options.ExpirationMinutes), cancellationToken);
        }

        return Ok(new SlidingCaptchaDto
        {
            Token = token,
            BackgroundImage = backgroundBase64,
            PuzzlePiece = puzzlePieceBase64,
            PieceY = pieceY
        });
    }

    /// <summary>
    /// 创建拼图形状路径（带凸起的拼图块）
    /// </summary>
    private static IPath CreateJigsawPath(int x, int y, int size)
    {
        var tabSize = size / 4;
        var builder = new PathBuilder();

        // 顶边（含凸起）
        builder.AddLine(new PointF(x, y), new PointF(x + size * 0.4f, y));
        builder.AddArc(
            new PointF(x + size * 0.5f, y - tabSize * 0.3f),
            tabSize * 0.5f, tabSize * 0.5f,
            0, 180, -180);
        builder.AddLine(new PointF(x + size * 0.6f, y), new PointF(x + size, y));

        // 右边（含凸起）
        builder.AddLine(new PointF(x + size, y), new PointF(x + size, y + size * 0.4f));
        builder.AddArc(
            new PointF(x + size + tabSize * 0.3f, y + size * 0.5f),
            tabSize * 0.5f, tabSize * 0.5f,
            0, -90, -180);
        builder.AddLine(new PointF(x + size, y + size * 0.6f), new PointF(x + size, y + size));

        // 底边
        builder.AddLine(new PointF(x + size, y + size), new PointF(x, y + size));

        // 左边
        builder.AddLine(new PointF(x, y + size), new PointF(x, y));

        builder.CloseFigure();
        return builder.Build();
    }

    /// <summary>
    /// 生成背景图片（含缺口）
    /// </summary>
    private static string GenerateBackgroundImage(int width, int height, IPath puzzlePath, bool addNoise, Random random)
    {
        using var image = new Image<Rgba32>(width, height);

        image.Mutate(ctx =>
        {
            // 渐变背景
            DrawGradientBackground(ctx, width, height, random);

            // 随机装饰元素
            DrawDecorations(ctx, width, height, random);

            // 额外噪声（高难度）
            if (addNoise)
            {
                DrawNoise(ctx, width, height, random);
            }

            // 绘制缺口（半透明暗色覆盖）
            ctx.Fill(new SolidBrush(new Color(new Rgba32(0, 0, 0, 80))), puzzlePath);

            // 缺口边框
            ctx.Draw(new SolidPen(new Color(new Rgba32(255, 255, 255, 128)), 2), puzzlePath);
        });

        return ImageToBase64(image);
    }

    /// <summary>
    /// 生成拼图块图片
    /// </summary>
    /// <remarks>
    /// <paramref name="random"/> 必须与 <see cref="GenerateBackgroundImage"/> 用同一个种子新建，
    /// 这样这里重画的完整底图才与背景逐像素一致（拼图块即背景缺口处被抠出的那一块）。
    /// </remarks>
    private static string GeneratePuzzlePiece(int width, int height, IPath puzzlePath, Random random)
    {
        // 重画一份与主背景相同的完整底图（同种子 ⇒ 同渐变、同装饰元素）
        using var fullImage = new Image<Rgba32>(width, height);

        fullImage.Mutate(ctx =>
        {
            DrawGradientBackground(ctx, width, height, random);
            DrawDecorations(ctx, width, height, random);
        });

        // 使用 Clip 方式裁剪拼图区域：在完整背景上用路径做遮罩
        var bounds = puzzlePath.Bounds;
        var cropX = (int)Math.Max(0, bounds.X);
        var cropY = (int)Math.Max(0, bounds.Y);
        var cropWidth = (int)Math.Min(bounds.Width + 2, width - cropX);
        var cropHeight = (int)Math.Min(bounds.Height + 2, height - cropY);

        // 创建透明画布，将裁剪区域绘制到其中
        using var pieceImage = new Image<Rgba32>(cropWidth, cropHeight);

        // 将路径平移到以裁剪区域左上角为原点
        var translatedPath = puzzlePath.Translate(-cropX, -cropY);

        pieceImage.Mutate(ctx =>
        {
            // 使用 Clip 限制绘制区域为拼图路径内部
            ctx.Clip(translatedPath, innerCtx =>
            {
                // 从完整背景图中绘制对应区域
                innerCtx.DrawImage(fullImage, new Point(-cropX, -cropY), 1f);
            });

            // 绘制拼图块边框
            ctx.Draw(new SolidPen(new Color(new Rgba32(255, 255, 255, 200)), 2), translatedPath);
        });

        return ImageToBase64(pieceImage);
    }

    /// <summary>
    /// 绘制渐变背景
    /// </summary>
    private static void DrawGradientBackground(IImageProcessingContext ctx, int width, int height, Random random)
    {
        // 生成随机的渐变颜色
        var r1 = (byte)random.Next(80, 200);
        var g1 = (byte)random.Next(80, 200);
        var b1 = (byte)random.Next(80, 200);
        var r2 = (byte)Math.Min(255, r1 + random.Next(30, 80));
        var g2 = (byte)Math.Min(255, g1 + random.Next(30, 80));
        var b2 = (byte)Math.Min(255, b1 + random.Next(30, 80));

        var color1 = new Color(new Rgba32(r1, g1, b1));
        var color2 = new Color(new Rgba32(r2, g2, b2));

        var brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(width, height),
            GradientRepetitionMode.None,
            new ColorStop(0, color1),
            new ColorStop(1, color2));

        ctx.Fill(brush, new RectangularPolygon(0, 0, width, height));
    }

    /// <summary>
    /// 绘制装饰元素（随机圆形和线条）
    /// </summary>
    private static void DrawDecorations(IImageProcessingContext ctx, int width, int height, Random random)
    {
        // 随机圆形
        for (var i = 0; i < 8; i++)
        {
            var cx = random.Next(0, width);
            var cy = random.Next(0, height);
            var radius = random.Next(10, 40);
            var alpha = (byte)random.Next(30, 80);
            var color = new Color(new Rgba32(
                (byte)random.Next(0, 255),
                (byte)random.Next(0, 255),
                (byte)random.Next(0, 255),
                alpha));

            ctx.Fill(color, new EllipsePolygon(cx, cy, radius));
        }

        // 随机线条
        for (var i = 0; i < 4; i++)
        {
            var x1 = random.Next(0, width);
            var y1 = random.Next(0, height);
            var x2 = random.Next(0, width);
            var y2 = random.Next(0, height);
            var alpha = (byte)random.Next(40, 100);
            var color = new Color(new Rgba32(
                (byte)random.Next(0, 255),
                (byte)random.Next(0, 255),
                (byte)random.Next(0, 255),
                alpha));

            ctx.DrawLine(new SolidPen(color, 1), new PointF(x1, y1), new PointF(x2, y2));
        }
    }

    /// <summary>
    /// 绘制噪声（高难度模式）
    /// </summary>
    private static void DrawNoise(IImageProcessingContext ctx, int width, int height, Random random)
    {
        // 额外的密集噪点
        for (var i = 0; i < 30; i++)
        {
            var cx = random.Next(0, width);
            var cy = random.Next(0, height);
            var radius = random.Next(2, 8);
            var color = new Color(new Rgba32(
                (byte)random.Next(0, 255),
                (byte)random.Next(0, 255),
                (byte)random.Next(0, 255),
                (byte)random.Next(60, 150)));

            ctx.Fill(color, new EllipsePolygon(cx, cy, radius));
        }

        // 额外密集线条
        for (var i = 0; i < 8; i++)
        {
            var x1 = random.Next(0, width);
            var y1 = random.Next(0, height);
            var x2 = random.Next(0, width);
            var y2 = random.Next(0, height);
            var color = new Color(new Rgba32(
                (byte)random.Next(0, 255),
                (byte)random.Next(0, 255),
                (byte)random.Next(0, 255),
                (byte)random.Next(60, 120)));

            ctx.DrawLine(new SolidPen(color, 2), new PointF(x1, y1), new PointF(x2, y2));
        }
    }

    /// <summary>
    /// 将图片转换为 Base64 字符串
    /// </summary>
    private static string ImageToBase64(Image<Rgba32> image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// 获取失败次数
    /// </summary>
    private async Task<long> GetFailureCountAsync(string clientId, CancellationToken cancellationToken)
    {
        if (_cache == null) return 0;
        var key = $"{FailureCacheKeyPrefix}{clientId}";
        // 计数由 IncrementAsync 以 long 写入，必须经 GetCounterAsync 读；
        // 用 GetAsync<int> 会因类型不匹配恒返回 0，难度自适应静默失效。
        return await _cache.GetCounterAsync(key, cancellationToken);
    }

    /// <summary>
    /// 增加失败计数
    /// </summary>
    private async Task IncrementFailureCountAsync(string clientId, CancellationToken cancellationToken)
    {
        if (_cache == null) return;
        var key = $"{FailureCacheKeyPrefix}{clientId}";
        try
        {
            // 原子递增：读-改-写在并发失败时会互相覆盖，导致难度升不上去（正是暴力破解的场景）
            await _cache.IncrementAsync(key, 1, TimeSpan.FromMinutes(FailureWindowMinutes), cancellationToken);
        }
        catch (Exception ex)
        {
            // 计数只驱动难度自适应；缓存故障不应把一次正常的"验证失败"变成 500
            Logger.LogWarning(ex, "Failed to increment sliding captcha failure count for client {ClientId}", clientId);
        }
    }

    /// <summary>
    /// 重置失败计数
    /// </summary>
    private async Task ResetFailureCountAsync(string clientId, CancellationToken cancellationToken)
    {
        if (_cache == null) return;
        var key = $"{FailureCacheKeyPrefix}{clientId}";
        await _cache.RemoveAsync(key, cancellationToken);
    }

    private static string GetCacheKey(string token) => $"{CacheKeyPrefix}{token}";
}

/// <summary>
/// 滑动验证码存储数据
/// </summary>
internal class SlidingCaptchaStoredData
{
    /// <summary>
    /// 正确的 X 坐标
    /// </summary>
    public int CorrectX { get; set; }

    /// <summary>
    /// 生成时决定的验证容差（像素）。0 表示未记录（旧令牌），验证时回退到调用方入参。
    /// </summary>
    public int Tolerance { get; set; }

    /// <summary>
    /// 客户端标识（用于自适应难度）
    /// </summary>
    public string? ClientId { get; set; }
}
