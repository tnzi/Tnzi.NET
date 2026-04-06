namespace Tnzi.AI.Rag.FileExtractors;

/// <summary>
/// 表格内容提取器 — 将 CSV/TSV 文件转换为结构化可检索文本。
/// 输出格式: "Table: {filename}\nHeaders: col1, col2\nRow 1: val1, val2\n..."
/// </summary>
public class TableContentExtractor : IFileExtractorService
{
    private static readonly HashSet<string> SupportedExtensions = [".csv", ".tsv"];

    /// <inheritdoc />
    public bool Supports(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext.ToLowerInvariant());
    }

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        Check.NotNull(content);
        Check.NotNullOrWhiteSpace(fileName);

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var delimiter = ext == ".tsv" ? '\t' : ',';

        using var reader = new StreamReader(content, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(ct);

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var sb = new StringBuilder();
        sb.Append($"Table: {Path.GetFileName(fileName)}");

        var rowIndex = 0;

        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseFields(line, delimiter);

            if (rowIndex == 0)
            {
                sb.Append($"\nHeaders: {string.Join(", ", fields)}");
            }
            else
            {
                sb.Append($"\nRow {rowIndex}: {string.Join(", ", fields)}");
            }

            rowIndex++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// 解析单行字段，支持带引号的字段（处理字段内含分隔符和换行的情况）
    /// </summary>
    private static string[] ParseFields(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    // 双引号转义
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }
}
