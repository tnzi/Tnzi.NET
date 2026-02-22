namespace Tnzi.Storage.Extensions;

/// <summary>
/// 文件字段扩展方法
/// </summary>
public static class FileFieldExtensions
{
    /// <summary>
    /// 解析文件ID（支持逗号分隔和JSON数组格式）
    /// Request 时可能传递 "guid1,guid2,guid3" 或 '["guid1","guid2","guid3"]'
    /// </summary>
    public static List<Guid> ParseFileIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<Guid>();

        // 尝试解析为JSON数组
        if (value.TrimStart().StartsWith("["))
        {
            try
            {
                var guids = JsonSerializer.Deserialize<List<string>>(value);
                if (guids != null)
                {
                    return guids.Select(Guid.Parse).ToList();
                }
            }
            catch
            {
                // 如果JSON解析失败，继续尝试逗号分隔
            }
        }

        // 解析为逗号分隔的字符串
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(Guid.Parse)
            .ToList();
    }

    /// <summary>
    /// 将文件ID列表转为JSON数组字符串（用于数据库存储）
    /// 数据库统一存储为JSON数组格式: '["guid1","guid2","guid3"]'
    /// </summary>
    public static string ToFileIdsJsonArray(IEnumerable<Guid> fileIds)
    {
        var list = fileIds.ToList();
        if (!list.Any())
            return "[]";

        return JsonSerializer.Serialize(list.Select(id => id.ToString()).ToList());
    }

    /// <summary>
    /// 从 JSON 数组字符串解析文件 ID（用于数据库读取）
    /// </summary>
    public static List<Guid> FromFileIdsJsonArray(string? jsonArray)
    {
        if (string.IsNullOrWhiteSpace(jsonArray) || jsonArray == "[]")
            return new List<Guid>();

        try
        {
            var guids = JsonSerializer.Deserialize<List<string>>(jsonArray);
            if (guids != null)
            {
                return guids.Select(Guid.Parse).ToList();
            }
        }
        catch
        {
            // 解析失败返回空列表
        }

        return new List<Guid>();
    }
}

