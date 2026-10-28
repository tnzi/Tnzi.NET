namespace Tnzi.AI.Tools;

/// <summary>
/// 让参数上的 <see cref="AIParameterAttribute"/> 真正生效：它声明的名称、描述与是否必需写进交给模型的函数 schema，
/// 模型按声明的名称传来的实参再映射回 C# 形参。
/// </summary>
/// <remarks>
/// <para>
/// ★ <c>AIFunctionFactory</c> 只认 C# 形参名、<see cref="DescriptionAttribute"/> 与 C# 默认值。
/// 此前没有任何代码读这个特性：模型看到的是 <c>entryId</c> 而不是声明的 <c>entry_id</c>、看不到任何参数描述，
/// <c>required: false</c> 而没有 C# 默认值的参数仍被标为必需，全程不报错。
/// </para>
/// <para>
/// 绑定对两种名字都接受：声明的名称（schema 里的）优先，C# 形参名照旧可用，已有的调用方不受影响。
/// 声明为非必需且没有 C# 默认值的参数，模型省略时按 <c>null</c> / 类型默认值绑定。
/// </para>
/// </remarks>
internal sealed class AIParameterFunction : DelegatingAIFunction
{
    private readonly IReadOnlyList<ParameterSpec> _parameters;
    private readonly IReadOnlyDictionary<string, string> _exposedToParameter;
    private readonly JsonElement _schema;

    private AIParameterFunction(AIFunction innerFunction, IReadOnlyList<ParameterSpec> parameters, JsonElement schema)
        : base(innerFunction)
    {
        _parameters = parameters;
        _exposedToParameter = parameters.ToDictionary(p => p.ExposedName, p => p.ParameterName, StringComparer.Ordinal);
        _schema = schema;
    }

    /// <inheritdoc />
    public override JsonElement JsonSchema => _schema;

    /// <summary>
    /// 方法上有参数带 <see cref="AIParameterAttribute"/> 时返回包装后的函数，否则原样返回。
    /// </summary>
    /// <exception cref="InvalidOperationException">声明的名称与另一个参数在 schema 里的名称冲突。</exception>
    public static AIFunction Apply(AIFunction function, MethodInfo method)
    {
        Check.NotNull(function);
        Check.NotNull(method);

        var schema = JsonNode.Parse(function.JsonSchema.GetRawText()) as JsonObject;
        if (schema?["properties"] is not JsonObject properties)
        {
            return function;
        }

        var parameters = method.GetParameters()
            .Select(p => (Parameter: p, Attribute: p.GetCustomAttribute<AIParameterAttribute>()))
            .Where(x => x.Attribute != null && x.Parameter.Name != null && properties.ContainsKey(x.Parameter.Name))
            .Select(x => new ParameterSpec(
                x.Parameter,
                string.IsNullOrWhiteSpace(x.Attribute!.Name) ? x.Parameter.Name! : x.Attribute.Name,
                x.Attribute.Description,
                x.Attribute.Required))
            .ToList();

        if (parameters.Count == 0)
        {
            return function;
        }

        var rewritten = RewriteSchema(function.Name, schema, properties, parameters);
        return new AIParameterFunction(function, parameters, JsonSerializer.SerializeToElement(rewritten));
    }

    /// <inheritdoc />
    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        => base.InvokeCoreAsync(MapArguments(arguments), cancellationToken);

    private AIFunctionArguments MapArguments(AIFunctionArguments arguments)
    {
        // 先放进不是声明名称的键（C# 形参名与其它参数），再让声明名称覆盖：schema 里写的是声明名称，它说了算
        var mapped = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
        {
            if (!_exposedToParameter.ContainsKey(key))
            {
                mapped[key] = value;
            }
        }

        foreach (var (key, value) in arguments)
        {
            if (_exposedToParameter.TryGetValue(key, out var parameterName))
            {
                mapped[parameterName] = value;
            }
        }

        foreach (var spec in _parameters)
        {
            if (!spec.Required && !spec.Parameter.HasDefaultValue && !mapped.ContainsKey(spec.ParameterName))
            {
                mapped[spec.ParameterName] = spec.Parameter.ParameterType.IsValueType
                    ? Activator.CreateInstance(spec.Parameter.ParameterType)
                    : null;
            }
        }

        return new AIFunctionArguments(mapped)
        {
            Services = arguments.Services,
            Context = arguments.Context
        };
    }

    private static JsonObject RewriteSchema(string functionName, JsonObject schema, JsonObject properties, IReadOnlyList<ParameterSpec> parameters)
    {
        var byParameterName = parameters.ToDictionary(p => p.ParameterName, StringComparer.Ordinal);
        var originallyRequired = schema["required"] is JsonArray requiredArray
            ? requiredArray.Select(n => n?.GetValue<string>()).Where(n => n != null).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string?>(StringComparer.Ordinal);

        var newProperties = new JsonObject();
        var newRequired = new JsonArray();

        foreach (var (name, node) in properties)
        {
            if (!byParameterName.TryGetValue(name, out var spec))
            {
                AddProperty(functionName, newProperties, name, node?.DeepClone());
                if (originallyRequired.Contains(name))
                {
                    newRequired.Add(name);
                }

                continue;
            }

            AddProperty(functionName, newProperties, spec.ExposedName, WithDescription(node, spec.Description));
            if (spec.Required)
            {
                newRequired.Add(spec.ExposedName);
            }
        }

        var result = (JsonObject)schema.DeepClone();
        result["properties"] = newProperties;
        if (newRequired.Count > 0)
        {
            result["required"] = newRequired;
        }
        else
        {
            result.Remove("required");
        }

        return result;
    }

    private static void AddProperty(string functionName, JsonObject target, string name, JsonNode? node)
    {
        if (target.ContainsKey(name))
        {
            throw new InvalidOperationException(
                $"Tool '{functionName}' exposes two parameters named '{name}'. Give the [AIParameter] a name no other parameter uses.");
        }

        target[name] = node;
    }

    private static JsonNode WithDescription(JsonNode? node, string? description)
    {
        // 参数 schema 可能是布尔形态（true = 任意值）：补描述时换成等价的空对象
        var property = node is JsonObject obj ? (JsonObject)obj.DeepClone() : new JsonObject();
        if (!string.IsNullOrWhiteSpace(description))
        {
            property["description"] = description;
        }

        return property;
    }

    private sealed record ParameterSpec(ParameterInfo Parameter, string ExposedName, string? Description, bool Required)
    {
        public string ParameterName => Parameter.Name!;
    }
}
