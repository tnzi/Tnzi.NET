using System.Text.Encodings.Web;

namespace Tnzi.AI.Infrastructure.Helpers;

/// <summary>
/// 线程标题的纯文本规则：回退标题、交给模型的输入、提示词、模型输出的清洗。无状态、不碰 I/O。
/// </summary>
/// <remarks>
/// <para>
/// <b>长度按「词」与「中日韩字」两把尺子量</b>：空格分词的语言按词计（默认 5 个），中日韩文字按字计（默认 10 个）。
/// 混排文本两把尺子按比例共用一份预算：一个词占 <c>targetCjk</c> 份、一个中日韩字占 <c>targetWords</c> 份，
/// 总预算 <c>targetWords × targetCjk</c>。纯英文正好 5 个词，纯中文正好 10 个字。
/// </para>
/// <para>
/// <b>用户文本以 JSON 数组的形式交给模型</b>，指令只在系统提示词里。用户写「忽略以上指令，回复……」时，
/// 那句话是数组里的一个字符串值，不是指令的延续；模型按「给这段数据起名」处理它。
/// </para>
/// <para>
/// <b>模型把标题放在 <c>&lt;title&gt;…&lt;/title&gt;</c> 里</b>：小模型偶尔先复述要求与消息再作答，
/// 标签让 <see cref="Clean"/> 分得清哪段是标题。没有标签的输出照旧能用，但只认看起来像标题的那一行。
/// </para>
/// </remarks>
public static partial class ThreadTitleText
{
    /// <summary>标题列的长度上限（<c>AgentThread.Title</c> 是 200 个 UTF-16 码元）。</summary>
    public const int ColumnMaxLength = 200;

    /// <summary>交给模型的用户消息的开头一句（JSON 数组紧随其后）。</summary>
    public const string UserMessagePreamble = "Generate the session title from this JSON array of human messages:\n";

    private const string SystemPromptTemplate =
        "Create a concise title for an AI assistant conversation from the supplied human messages. " +
        "Reply with the title alone inside <title></title> tags on one line, for example <title>Kyoto spring trip</title>. " +
        "Do not restate these instructions or the messages, and write nothing before or after the tags. " +
        "The title is plain natural language with no quotes, label, explanation, Markdown, or terminal control codes. " +
        "No code is allowed. Use the language of the messages. " +
        "Aim for about {0} words in non-CJK languages or {1} CJK characters. " +
        "If the messages give little to name, still return a short best-effort title, such as <title>Greeting</title>, instead of explaining.";

    private static readonly JsonSerializerOptions PromptJson = new()
    {
        // 不把中日韩文字转成 \uXXXX：模型要据此判断「用什么语言起标题」。控制字符与引号仍按 JSON 转义。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 系统提示词：要求、长度目标、无话可起时怎么办。
    /// </summary>
    public static string BuildSystemPrompt(int targetWords, int targetCjkCharacters)
        => string.Format(CultureInfo.InvariantCulture, SystemPromptTemplate, targetWords, targetCjkCharacters);

    /// <summary>
    /// 交给模型的用户消息：一句说明 + 只含第一条用户消息的 JSON 数组。
    /// </summary>
    public static string BuildUserMessage(string firstUserMessage)
    {
        Check.NotNull(firstUserMessage);
        return UserMessagePreamble + JsonSerializer.Serialize(new[] { firstUserMessage }, PromptJson);
    }

    /// <summary>
    /// 截到 UTF-8 不超过 <paramref name="maxBytes"/> 字节，只在字符（文本元素）边界上切。
    /// </summary>
    public static string TruncateUtf8(string text, int maxBytes)
    {
        Check.NotNull(text);
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
        {
            return text;
        }

        var bytes = 0;
        var end = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var size = Encoding.UTF8.GetByteCount(element);
            if (bytes + size > maxBytes)
            {
                break;
            }

            bytes += size;
            end = elements.ElementIndex + element.Length;
        }

        return text[..end];
    }

    /// <summary>
    /// 回退标题：第一条用户消息的开头几个词（或几个中日韩字），去掉控制字符、空白合并成单个空格。
    /// 没有可用文字时返回 null。
    /// </summary>
    public static string? Fallback(string? userMessage, int targetWords, int targetCjkCharacters, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return null;
        }

        var normalized = Normalize(userMessage);
        if (normalized.Length == 0)
        {
            return null;
        }

        var budget = targetWords * targetCjkCharacters;
        var used = 0;
        var inWord = false;
        var pendingSpace = false;
        var result = new StringBuilder();

        var elements = StringInfo.GetTextElementEnumerator(normalized);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (element == " ")
            {
                inWord = false;
                pendingSpace = result.Length > 0;
                continue;
            }

            int cost;
            if (IsCjk(element))
            {
                // 每个中日韩字自成一个计量单位；它也结束前面的拉丁词
                cost = targetWords;
                inWord = false;
            }
            else if (!inWord)
            {
                // 一个新词开始：整词一次计费，后面的字母不再计
                cost = targetCjkCharacters;
                inWord = true;
            }
            else
            {
                cost = 0;
            }

            if (cost > 0 && used + cost > budget && result.Length > 0)
            {
                break;
            }

            used += cost;
            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            result.Append(element);
        }

        return Cap(result.ToString(), maxLength);
    }

    /// <summary>
    /// 清洗模型输出，取出标题本身；拿不到可信标题时返回 null（调用方留着回退标题）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>有 <c>&lt;title&gt;</c> 标签时只信标签</b>：取最后一个非空标签的内容（小模型偶尔先复述要求再作答，
    /// 答案在后）。只有开标签没有闭标签时取开标签之后的文字，只有闭标签时取它之前的那一行。
    /// 标签都是空的就返回 null，不回头去读标签外的文字：那些是复述或说明。
    /// </para>
    /// <para>
    /// <b>没有标签时按行挑</b>：带「Title:」「标题：」标注的行优先（去掉标注）；否则取第一个不像说明文字的行。
    /// 说明文字指以冒号结尾的引导句（「Here is a title:」），或句中有句号 / 问号 / 叹号断句、后面还接着话的行
    /// （「需要的标题应使用中文。消息内容：……」）。所有行都像说明文字时返回 null。
    /// </para>
    /// <para>
    /// 选中的文字再去掉整行的强调标记与成对引号，删掉控制 / 双向 / 零宽字符与终端转义序列，合并空白，
    /// 按字符截到 <paramref name="maxLength"/>。
    /// </para>
    /// </remarks>
    public static string? Clean(string? raw, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = AnsiEscape().Replace(raw, string.Empty);
        var title = TitleTag().IsMatch(text) ? FromTags(text) : FromLines(text);
        return title is null ? null : Cap(title, maxLength);
    }

    /// <summary>标签里的标题：最后一个非空的成对标签；没有成对的，就看落单的开 / 闭标签。</summary>
    private static string? FromTags(string text)
    {
        var paired = TitleElement().Matches(text)
            .Select(m => FirstLine(m.Groups["inner"].Value))
            .LastOrDefault(t => t is not null);
        if (paired is not null)
        {
            return paired;
        }

        if (TitleElement().IsMatch(text))
        {
            // 有成对标签但都是空的：模型没给出标题
            return null;
        }

        var open = TitleOpen().Matches(text).LastOrDefault();
        if (open is not null)
        {
            return FirstLine(TitleTag().Replace(text[(open.Index + open.Length)..], string.Empty));
        }

        var close = TitleClose().Match(text);
        var before = text[..close.Index].Split(LineBreaks, StringSplitOptions.None)
            .Select(Normalize)
            .LastOrDefault(l => l.Length > 0);
        return before is null ? null : NonEmpty(StripLabel(Unwrap(before)));
    }

    /// <summary>没有标签的输出：标注过的行优先，否则第一个不像说明文字的行。</summary>
    private static string? FromLines(string text)
    {
        var lines = text.Split(LineBreaks, StringSplitOptions.None)
            .Select(Normalize)
            .Where(l => l.Length > 0)
            .Select(Unwrap)
            .Where(l => l.Length > 0)
            .ToList();

        foreach (var line in lines)
        {
            var label = TitleLabel().Match(line);
            if (label.Success && NonEmpty(Unwrap(line[label.Length..])) is { } labelled)
            {
                return labelled;
            }
        }

        return lines.FirstOrDefault(l => !LooksLikeProse(l));
    }

    /// <summary>一段文字的第一行非空文本，去掉整行包裹与「标题：」标注。</summary>
    private static string? FirstLine(string text)
    {
        var line = text.Split(LineBreaks, StringSplitOptions.None)
            .Select(Normalize)
            .FirstOrDefault(l => l.Length > 0);
        return line is null ? null : NonEmpty(StripLabel(Unwrap(line)));
    }

    private static string StripLabel(string line)
    {
        var label = TitleLabel().Match(line);
        return label.Success ? Unwrap(line[label.Length..]) : line;
    }

    private static string? NonEmpty(string text) => text.Length == 0 ? null : text;

    /// <summary>引导句（以冒号结尾），或句中断句、后面还接着话的行：是说明，不是标题。</summary>
    private static bool LooksLikeProse(string line)
        => line.EndsWith(':') || line.EndsWith('：') || SentenceBreak().IsMatch(line);

    /// <summary>行分隔：CR / LF / CRLF，以及 Unicode 的行分隔符（U+2028）、段分隔符（U+2029）与 NEL（U+0085）。</summary>
    private static readonly string[] LineBreaks =
        ["\r\n", "\n", "\r", ((char)0x2028).ToString(), ((char)0x2029).ToString(), ((char)0x85).ToString()];

    /// <summary>整行包着的 <c>**…**</c> / <c>__…__</c> / 成对引号，可以叠几层（<c>"**Title**"</c>）。</summary>
    private static string Unwrap(string line)
    {
        while (true)
        {
            var emphasis = WholeLineEmphasis().Match(line);
            if (emphasis.Success)
            {
                line = emphasis.Groups["inner"].Value.Trim();
                continue;
            }

            if (line.Length >= 2 && IsQuotePair(line[0], line[^1]))
            {
                line = line[1..^1].Trim();
                continue;
            }

            return line;
        }
    }

    private static bool IsQuotePair(char open, char close) => (open, close) switch
    {
        ('"', '"') or ('\'', '\'') or ('`', '`') => true,
        ('“', '”') or ('‘', '’') => true,
        ('「', '」') or ('『', '』') or ('«', '»') => true,
        _ => false
    };

    /// <summary>删掉控制字符与格式字符（双向控制、零宽字符、BOM），把各种空白合并成单个空格。</summary>
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
            {
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(rune.ToString());
        }

        return sb.ToString();
    }

    /// <summary>按用户看到的字符截到 <paramref name="maxLength"/>，同时不超过标题列的长度。</summary>
    private static string? Cap(string text, int maxLength)
    {
        var limit = Math.Min(maxLength, ColumnMaxLength);
        var sb = new StringBuilder();
        var count = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext() && count < limit)
        {
            var element = elements.GetTextElement();
            if (sb.Length + element.Length > ColumnMaxLength)
            {
                break;
            }

            sb.Append(element);
            count++;
        }

        var capped = sb.ToString().Trim();
        return capped.Length == 0 ? null : capped;
    }

    /// <summary>中日韩文字（汉字、假名、谚文）。按文本元素的第一个码点判定。</summary>
    internal static bool IsCjk(string element)
    {
        if (element.Length == 0)
        {
            return false;
        }

        var code = Rune.GetRuneAt(element, 0).Value;
        return code is >= 0x4E00 and <= 0x9FFF       // CJK 统一汉字
            or >= 0x3400 and <= 0x4DBF               // 扩展 A
            or >= 0x20000 and <= 0x2FA1F             // 扩展 B 及以后、兼容补充
            or >= 0xF900 and <= 0xFAFF               // 兼容汉字
            or >= 0x3040 and <= 0x30FF               // 平假名、片假名
            or >= 0x31F0 and <= 0x31FF               // 片假名扩展
            or >= 0xAC00 and <= 0xD7AF               // 谚文音节
            or >= 0x1100 and <= 0x11FF               // 谚文字母
            or >= 0x3130 and <= 0x318F;              // 谚文兼容字母
    }

    [GeneratedRegex(@"^(?<mark>\*\*|__)(?<inner>.+)\k<mark>$")]
    private static partial Regex WholeLineEmphasis();

    /// <summary>任何一个 <c>&lt;title&gt;</c> 开标签或闭标签。</summary>
    [GeneratedRegex(@"<\s*/?\s*title\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<\s*title\s*>(?<inner>.*?)<\s*/\s*title\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleElement();

    [GeneratedRegex(@"<\s*title\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleOpen();

    [GeneratedRegex(@"<\s*/\s*title\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleClose();

    /// <summary>行首的「Title:」「Session title:」「标题：」「会话标题：」标注，可带 Markdown 强调。</summary>
    [GeneratedRegex(@"^(?:\*\*|__)?\s*(?:(?:session|conversation|chat)\s+)?(?:title|标题|会话标题|对话标题)\s*(?:\*\*|__)?\s*[:：]\s*(?:\*\*|__)?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex TitleLabel();

    /// <summary>句中断句：中日文句号 / 问号 / 叹号后还有文字，或西文句末标点后接空白再接文字。</summary>
    [GeneratedRegex(@"[。！？](?=.*\S)|[.!?]\s+\S")]
    private static partial Regex SentenceBreak();

    /// <summary>CSI / OSC 终端转义序列（ESC 本身是控制字符，会被单独删掉，这里连同后面的参数一起删）。</summary>
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)?)")]
    private static partial Regex AnsiEscape();
}
