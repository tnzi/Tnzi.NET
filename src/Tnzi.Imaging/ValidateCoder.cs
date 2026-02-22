
namespace Tnzi.Imaging;

/// <summary>
/// 验证码生成类（使用SixLabors.ImageSharp实现，支持跨平台）
/// </summary>
public class ValidateCoder
{
    /// <summary>
    /// 初始化<see cref="ValidateCoder"/>类的新实例
    /// </summary>
    public ValidateCoder()
    {
        FontNames = new List<string> { "Arial", "Times New Roman", "Courier New", "Verdana", "Georgia" };
        FontNamesForHanzi = new List<string> { "SimSun", "SimHei", "KaiTi", "FangSong", "Microsoft YaHei" };
        FontSize = 20;
        FontWidth = FontSize;
        BgColor = Color.FromRgb(240, 240, 240);
        RandomPointPercent = 0;
        RandomLineCount = 0;
    }

    #region 属性

    /// <summary>
    /// 获取或设置 字体名称集合
    /// </summary>
    public List<string> FontNames { get; set; }

    /// <summary>
    /// 获取或设置 汉字字体名称集合
    /// </summary>
    public List<string> FontNamesForHanzi { get; set; }

    /// <summary>
    /// 获取或设置 字体大小
    /// </summary>
    public int FontSize { get; set; }

    /// <summary>
    /// 获取或设置 字体宽度
    /// </summary>
    public int FontWidth { get; set; }

    /// <summary>
    /// 获取或设置 图片高度
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 获取或设置 背景颜色
    /// </summary>
    public Color BgColor { get; set; }

    /// <summary>
    /// 获取或设置 是否有边框
    /// </summary>
    public bool HasBorder { get; set; }

    /// <summary>
    /// 获取或设置 是否随机位置
    /// </summary>
    public bool RandomPosition { get; set; }

    /// <summary>
    /// 获取或设置 是否随机字体颜色
    /// </summary>
    public bool RandomColor { get; set; }

    /// <summary>
    /// 获取或设置 是否随机倾斜字体
    /// </summary>
    public bool RandomItalic { get; set; }

    /// <summary>
    /// 获取或设置 随机干扰点百分比（百分数形式）
    /// </summary>
    public double RandomPointPercent { get; set; }

    /// <summary>
    /// 获取或设置 随机干扰线数量
    /// </summary>
    public int RandomLineCount { get; set; }

    #endregion

    #region 公共方法

    /// <summary>
    /// 获取指定长度的验证码字符串
    /// </summary>
    /// <param name="length">验证码长度</param>
    /// <param name="codeType">验证码类型</param>
    /// <returns>验证码字符串</returns>
    public string GetCode(int length, ValidateCodeType codeType = ValidateCodeType.NumberAndLetter)
    {
        Check.GreaterThan(length, 0);

        return codeType switch
        {
            ValidateCodeType.Number => GetRandomNums(length),
            ValidateCodeType.Hanzi => GetRandomHanzis(length),
            _ => GetRandomNumsAndLetters(length)
        };
    }

    /// <summary>
    /// 获取指定字符串的验证码图片（返回字节数组）
    /// </summary>
    /// <param name="code">验证码字符串</param>
    /// <param name="codeType">验证码类型</param>
    /// <returns>验证码图片的字节数组（PNG格式）</returns>
    public byte[] CreateImageBytes(string code, ValidateCodeType codeType)
    {
        Check.NotNullOrEmpty(code);

        using var image = CreateImageInternal(code, codeType);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// 获取指定字符串的验证码图片（返回Image对象）
    /// </summary>
    /// <param name="code">验证码字符串</param>
    /// <param name="codeType">验证码类型</param>
    /// <returns>验证码图片</returns>
    public Image<Rgba32> CreateImage(string code, ValidateCodeType codeType)
    {
        Check.NotNullOrEmpty(code);

        return CreateImageInternal(code, codeType);
    }

    /// <summary>
    /// 获取指定长度的验证码图片
    /// </summary>
    /// <param name="length">验证码长度</param>
    /// <param name="code">输出的验证码字符串</param>
    /// <param name="codeType">验证码类型</param>
    /// <returns>验证码图片的字节数组（PNG格式）</returns>
    public byte[] CreateImageBytes(int length, out string code, ValidateCodeType codeType = ValidateCodeType.NumberAndLetter)
    {
        Check.GreaterThan(length, 0);

        code = GetCode(length, codeType);
        return CreateImageBytes(code, codeType);
    }

    /// <summary>
    /// 获取指定长度的验证码图片
    /// </summary>
    /// <param name="length">验证码长度</param>
    /// <param name="code">输出的验证码字符串</param>
    /// <param name="codeType">验证码类型</param>
    /// <returns>验证码图片</returns>
    public Image<Rgba32> CreateImage(int length, out string code, ValidateCodeType codeType = ValidateCodeType.NumberAndLetter)
    {
        Check.GreaterThan(length, 0);

        code = GetCode(length, codeType);
        return CreateImage(code, codeType);
    }

    #endregion

    #region 私有方法

    private Image<Rgba32> CreateImageInternal(string code, ValidateCodeType codeType)
    {
        int width = FontWidth * code.Length + FontWidth;
        int height = Height > 0 ? Height : FontSize + FontSize / 2;

        var image = new Image<Rgba32>(width, height);
        image.Mutate(ctx =>
        {
            // 填充背景色
            ctx.Fill(BgColor);

            // 绘制边框
            if (HasBorder)
            {
                ctx.Draw(Color.Silver, 1, new RectangleF(0, 0, width - 1, height - 1));
            }

            // 绘制干扰线
            for (int i = 0; i < RandomLineCount; i++)
            {
                int x1 = Random.Shared.Next(width);
                int y1 = Random.Shared.Next(height);
                int x2 = Random.Shared.Next(width);
                int y2 = Random.Shared.Next(height);

                Color lineColor = RandomColor
                    ? Color.FromRgb((byte)Random.Shared.Next(70, 200), (byte)Random.Shared.Next(70, 200), (byte)Random.Shared.Next(70, 200))
                    : Color.FromRgb(90, 90, 90);

                ctx.DrawLine(lineColor, 2, new SixLabors.ImageSharp.PointF(x1, y1), new SixLabors.ImageSharp.PointF(x2, y2));
            }

            // 绘制干扰点
            int pointCount = (int)(width * height * RandomPointPercent / 100);
            for (int i = 0; i < pointCount; i++)
            {
                int x = Random.Shared.Next(width);
                int y = Random.Shared.Next(height);
                Color pointColor = RandomColor
                    ? Color.FromRgb((byte)Random.Shared.Next(100, 200), (byte)Random.Shared.Next(100, 200), (byte)Random.Shared.Next(100, 200))
                    : Color.FromRgb(100, 100, 100);

                ctx.Fill(pointColor, new RectangleF(x, y, 2, 2));
            }

            // 绘制验证码字符
            // 使用字体回退机制：优先使用 FontNames 中的字体
            var fontFamily = GetAvailableFontFamily(FontNames);
            Font font = fontFamily.CreateFont(FontSize, FontStyle.Bold);

            for (int i = 0; i < code.Length; i++)
            {
                string charStr = code[i].ToString();
                float x = RandomPosition ? Random.Shared.Next(FontWidth / 2) + i * FontWidth : i * FontWidth + FontWidth / 4;
                float y = RandomPosition ? Random.Shared.Next(FontSize / 2) + FontSize / 2 : FontSize;

                Rgba32 bgRgba = BgColor.ToPixel<Rgba32>();
                Color charColor = RandomColor
                    ? Color.FromRgb((byte)Random.Shared.Next(50, 150), (byte)Random.Shared.Next(50, 150), (byte)Random.Shared.Next(50, 150))
                    : Color.FromRgb((byte)(255 - bgRgba.R), (byte)(255 - bgRgba.G), (byte)(255 - bgRgba.B));

                var textOptions = new RichTextOptions(font)
                {
                    Origin = new PointF(x, y),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };

                if (RandomItalic)
                {
                    // 应用倾斜变换 - 使用DrawingOptions的Transform
                    var drawingOptions = new DrawingOptions
                    {
                        Transform = System.Numerics.Matrix3x2.CreateSkew((float)(Random.Shared.Next(2, 9) / 10.0 - 0.5), 0.001f)
                    };
                    ctx.DrawText(drawingOptions, textOptions, charStr, Brushes.Solid(charColor), Pens.Solid(charColor, 1));
                }
                else
                {
                    ctx.DrawText(textOptions, charStr, Brushes.Solid(charColor), Pens.Solid(charColor, 1));
                }
            }
        });

        return image;
    }

    private static string GetRandomNums(int length)
    {
        var nums = new int[length];
        for (int i = 0; i < length; i++)
        {
            nums[i] = Random.Shared.Next(0, 10);
        }
        return nums.ExpandAndToString("");
    }

    // 缓存可用字符数组，避免每次调用都 Split
    private static readonly string[] AllChars = "2,3,4,5,6,7,8,9,A,B,C,D,E,F,G,H,J,K,M,N,P,Q,R,S,T,U,V,W,X,Y,Z,a,b,c,d,e,f,g,h,k,m,n,p,q,r,s,t,u,v,w,x,y,z".Split(',');

    private static string GetRandomNumsAndLetters(int length)
    {
        // 允许重复字符，避免 length > 可用字符数时死循环
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = AllChars[Random.Shared.Next(AllChars.Length)][0];
        }
        return new string(result);
    }

    /// <summary>
    /// 获取汉字验证码
    /// </summary>
    /// <param name="length">验证码长度</param>
    /// <returns>汉字验证码字符串</returns>
    private static string GetRandomHanzis(int length)
    {
        // 常用汉字Unicode范围：\u4e00-\u9fa5
        var result = new StringBuilder();
        for (int i = 0; i < length; i++)
        {
            int unicode = Random.Shared.Next(0x4e00, 0x9fa5);
            result.Append((char)unicode);
        }
        return result.ToString();
    }

    /// <summary>
    /// 从字体名称列表中获取第一个可用的字体族，回退到系统任意可用字体
    /// </summary>
    private static FontFamily GetAvailableFontFamily(List<string> fontNames)
    {
        foreach (var name in fontNames)
        {
            if (SystemFonts.TryGet(name, out var family))
                return family;
        }
        var families = SystemFonts.Families;
        if (!families.Any())
            throw new InvalidOperationException("No fonts available on the system.");
        return families.First();
    }

    #endregion
}
