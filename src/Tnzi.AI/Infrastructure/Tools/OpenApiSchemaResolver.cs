namespace Tnzi.AI.Infrastructure.Tools;

/// <summary>
/// 解开 OpenAPI 文档里的本地 <c>$ref</c>（<c>#/components/...</c>），得到可以单独交给模型的 JSON Schema。
/// </summary>
/// <remarks>
/// <para>
/// 工具的参数 schema 是一份独立文档，里面不能留指向 OpenAPI 文档其他位置的引用：
/// 提供商要么因为解析不了引用拒绝整个请求，要么模型只看到一个空引用、不知道有哪些字段。
/// </para>
/// <para>
/// 自引用与互相引用（树、链表）展开到出现环为止；环处、外部文件引用与指针失配的引用替换成不加约束的 schema，
/// 原引用名写进 description。
/// </para>
/// </remarks>
internal static class OpenApiSchemaResolver
{
    /// <summary>JSON 嵌套层数上限：防御畸形或超深的规范把生成过程拖进深递归。</summary>
    private const int MaxDepth = 64;

    /// <summary>
    /// 解开元素自身的 <c>$ref</c>（参数对象、请求体对象这一层引用 <c>#/components/parameters</c> 等），不递归进内部。
    /// 解析不了时原样返回。
    /// </summary>
    public static JsonElement ResolveReference(JsonElement element, JsonElement root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = element;
        while (TryGetReference(current, out var reference))
        {
            if (!seen.Add(reference) || !TryResolvePointer(root, reference, out var target))
            {
                return current;
            }

            current = target;
        }

        return current;
    }

    /// <summary>递归内联 <paramref name="schema"/> 中所有本地 <c>$ref</c>，返回与原文档无关的新元素。</summary>
    public static JsonElement Inline(JsonElement schema, JsonElement root)
    {
        var node = InlineNode(schema, root, new HashSet<string>(StringComparer.Ordinal), 0);
        return node == null
            ? JsonSerializer.SerializeToElement(new JsonObject())
            : JsonSerializer.SerializeToElement(node);
    }

    private static JsonNode? InlineNode(JsonElement element, JsonElement root, HashSet<string> activeReferences, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object when TryGetReference(element, out var reference):
                return InlineReference(element, reference, root, activeReferences, depth);

            case JsonValueKind.Object:
                var obj = new JsonObject();
                foreach (var property in element.EnumerateObject())
                {
                    obj[property.Name] = depth < MaxDepth
                        ? InlineNode(property.Value, root, activeReferences, depth + 1)
                        : new JsonObject();
                }
                return obj;

            case JsonValueKind.Array:
                var array = new JsonArray();
                foreach (var item in element.EnumerateArray())
                {
                    array.Add(depth < MaxDepth ? InlineNode(item, root, activeReferences, depth + 1) : new JsonObject());
                }
                return array;

            default:
                return JsonNode.Parse(element.GetRawText());
        }
    }

    private static JsonNode InlineReference(
        JsonElement element, string reference, JsonElement root, HashSet<string> activeReferences, int depth)
    {
        JsonObject resolved;
        if (depth < MaxDepth
            && !activeReferences.Contains(reference)
            && TryResolvePointer(root, reference, out var target))
        {
            activeReferences.Add(reference);
            var inlined = InlineNode(target, root, activeReferences, depth + 1);
            activeReferences.Remove(reference);

            if (inlined is not JsonObject inlinedObject)
            {
                return inlined ?? new JsonObject();
            }

            resolved = inlinedObject;
        }
        else
        {
            resolved = new JsonObject { ["description"] = $"Schema '{ReferenceName(reference)}' (not expanded)" };
        }

        // OpenAPI 3.1 允许 $ref 旁边带 description 等关键字：本地写的覆盖被引用的
        foreach (var property in element.EnumerateObject())
        {
            if (!property.NameEquals("$ref"))
            {
                resolved[property.Name] = InlineNode(property.Value, root, activeReferences, depth + 1);
            }
        }

        return resolved;
    }

    private static bool TryGetReference(JsonElement element, out string reference)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("$ref", out var refElement)
            && refElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(refElement.GetString()))
        {
            reference = refElement.GetString()!;
            return true;
        }

        reference = string.Empty;
        return false;
    }

    /// <summary>按 JSON Pointer（RFC 6901）解析文档内引用；只认 <c>#/</c> 开头的本地引用。</summary>
    private static bool TryResolvePointer(JsonElement root, string reference, out JsonElement target)
    {
        target = default;
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return false;
        }

        var current = root;
        foreach (var rawSegment in reference[2..].Split('/'))
        {
            var segment = Uri.UnescapeDataString(rawSegment).Replace("~1", "/").Replace("~0", "~");
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var next))
            {
                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array
                     && int.TryParse(segment, out var index)
                     && index >= 0 && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else
            {
                return false;
            }
        }

        target = current;
        return true;
    }

    private static string ReferenceName(string reference)
    {
        var slash = reference.LastIndexOf('/');
        return slash >= 0 && slash < reference.Length - 1 ? reference[(slash + 1)..] : reference;
    }
}
