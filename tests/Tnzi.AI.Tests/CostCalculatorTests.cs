namespace Tnzi.AI.Tests;

/// <summary>
/// CostCalculator 单元测试
/// </summary>
public class CostCalculatorTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CostCalculator CreateCalculator(CostTrackingOptions? costOptions = null)
    {
        var aiOptions = new AIOptions
        {
            CostTracking = costOptions ?? new CostTrackingOptions { Enabled = true }
        };
        var monitor = new Mock<IOptionsMonitor<AIOptions>>();
        monitor.Setup(x => x.CurrentValue).Returns(aiOptions);
        return new CostCalculator(monitor.Object);
    }

    private static CostCalculator CreateCalculatorFromJson(string json)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();
        var costOptions = configuration.GetSection("AI:CostTracking")
            .Get<CostTrackingOptions>(o => o.ErrorOnUnknownConfiguration = true)!;
        return CreateCalculator(costOptions);
    }

    // -------------------------------------------------------------------------
    // Binding from appsettings
    // -------------------------------------------------------------------------

    /// <summary>
    /// ★ <c>:</c> 是 IConfiguration 的路径分隔符。文档一直写 <c>"OpenAI:gpt-4o"</c> 作字典键，
    /// 绑定器却把它拆成 <c>ModelCosts:OpenAI:gpt-4o</c>：键 <c>OpenAI</c> 得到一个全 0 的费率，
    /// <c>gpt-4o</c> 被当未知属性丢弃 —— 从 appsettings 里从来没有一条费率绑进来过，
    /// 而所有既有测试都用 C# 直接构造字典，全绿。两级字典让扁平键与嵌套写法在配置层等价。
    /// </summary>
    [Fact]
    public void CalculateCost_FromAppSettings_FlatProviderColonModelKey_Binds()
    {
        var calc = CreateCalculatorFromJson("""
            { "AI": { "CostTracking": { "Enabled": true, "ModelCosts": {
                "OpenAI:gpt-4o": { "InputCostPer1MTokens": 5.0, "OutputCostPer1MTokens": 15.0 },
                "Anthropic:*": { "InputCostPer1MTokens": 2.0, "OutputCostPer1MTokens": 8.0 }
            } } } }
            """);

        calc.CalculateCost("OpenAI", "gpt-4o", 1000, 500).ShouldBe(0.0125m);
        calc.CalculateCost("Anthropic", "claude-opus-5", 1000, 500).ShouldBe(0.006m);
        calc.CalculateCost("OpenAI", "unknown", 1000, 500).ShouldBeNull();
    }

    [Fact]
    public void CalculateCost_FromAppSettings_NestedProviderThenModel_Binds()
    {
        var calc = CreateCalculatorFromJson("""
            { "AI": { "CostTracking": { "Enabled": true, "ModelCosts": {
                "OpenAI": { "gpt-4o": { "InputCostPer1MTokens": 5.0, "OutputCostPer1MTokens": 15.0 } }
            } } } }
            """);

        calc.CalculateCost("OpenAI", "gpt-4o", 1000, 500).ShouldBe(0.0125m);
    }

    /// <summary>绑定器建出来的内层字典不带比较器；provider / model 名的大小写不能影响命中。</summary>
    [Fact]
    public void CalculateCost_FromAppSettings_LookupIsCaseInsensitive()
    {
        var calc = CreateCalculatorFromJson("""
            { "AI": { "CostTracking": { "Enabled": true, "ModelCosts": {
                "openai:GPT-4O": { "InputCostPer1MTokens": 5.0, "OutputCostPer1MTokens": 15.0 }
            } } } }
            """);

        calc.CalculateCost("OpenAI", "gpt-4o", 1000, 500).ShouldBe(0.0125m);
    }

    // -------------------------------------------------------------------------
    // Enabled / Disabled
    // -------------------------------------------------------------------------

    [Fact]
    public void CalculateCost_WhenDisabled_ReturnsNull()
    {
        var calc = CreateCalculator(new CostTrackingOptions { Enabled = false });

        var cost = calc.CalculateCost("OpenAI", "gpt-4o", 1000, 500);

        cost.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Exact match
    // -------------------------------------------------------------------------

    [Fact]
    public void CalculateCost_ExactMatch_ReturnsCorrectCost()
    {
        var costOptions = new CostTrackingOptions
        {
            Enabled = true,
            ModelCosts = new(StringComparer.OrdinalIgnoreCase)
            {
                ["OpenAI"] = new()
                {
                    ["gpt-4o"] = new ModelCostRate
                    {
                        InputCostPer1MTokens = 5.0m,
                        OutputCostPer1MTokens = 15.0m
                    }
                }
            }
        };
        var calc = CreateCalculator(costOptions);

        // (1000 * 5.0 + 500 * 15.0) / 1_000_000 = (5000 + 7500) / 1_000_000 = 0.0125
        var cost = calc.CalculateCost("OpenAI", "gpt-4o", 1000, 500);

        cost.ShouldNotBeNull();
        cost!.Value.ShouldBe(0.0125m);
    }

    [Fact]
    public void CalculateCost_ExactMatch_CaseInsensitive()
    {
        var costOptions = new CostTrackingOptions
        {
            Enabled = true,
            // 刻意不给比较器：绑定器建出来的内层字典就是这个形态。
            ModelCosts = new()
            {
                ["openai"] = new()
                {
                    ["GPT-4O"] = new ModelCostRate
                    {
                        InputCostPer1MTokens = 5.0m,
                        OutputCostPer1MTokens = 15.0m
                    }
                }
            }
        };
        var calc = CreateCalculator(costOptions);

        var cost = calc.CalculateCost("OpenAI", "gpt-4o", 1000, 500);

        cost.ShouldNotBeNull();
    }

    // -------------------------------------------------------------------------
    // Wildcard match
    // -------------------------------------------------------------------------

    [Fact]
    public void CalculateCost_WildcardMatch_UsedWhenNoExactMatch()
    {
        var costOptions = new CostTrackingOptions
        {
            Enabled = true,
            ModelCosts = new(StringComparer.OrdinalIgnoreCase)
            {
                ["OpenAI"] = new()
                {
                    ["*"] = new ModelCostRate
                    {
                        InputCostPer1MTokens = 2.0m,
                        OutputCostPer1MTokens = 8.0m
                    }
                }
            }
        };
        var calc = CreateCalculator(costOptions);

        // (1000 * 2.0 + 500 * 8.0) / 1_000_000 = (2000 + 4000) / 1_000_000 = 0.006
        var cost = calc.CalculateCost("OpenAI", "unknown-model", 1000, 500);

        cost.ShouldNotBeNull();
        cost!.Value.ShouldBe(0.006m);
    }

    [Fact]
    public void CalculateCost_ExactMatchTakesPriorityOverWildcard()
    {
        var costOptions = new CostTrackingOptions
        {
            Enabled = true,
            ModelCosts = new(StringComparer.OrdinalIgnoreCase)
            {
                ["OpenAI"] = new()
                {
                    ["gpt-4o"] = new ModelCostRate { InputCostPer1MTokens = 5.0m, OutputCostPer1MTokens = 15.0m },
                    ["*"] = new ModelCostRate { InputCostPer1MTokens = 1.0m, OutputCostPer1MTokens = 1.0m }
                }
            }
        };
        var calc = CreateCalculator(costOptions);

        // Should use exact match: (1000*5 + 0*15) / 1_000_000 = 0.005
        var cost = calc.CalculateCost("OpenAI", "gpt-4o", 1000, 0);

        cost.ShouldNotBeNull();
        cost!.Value.ShouldBe(0.005m);
    }

    // -------------------------------------------------------------------------
    // Default rate
    // -------------------------------------------------------------------------

    [Fact]
    public void CalculateCost_DefaultRate_UsedWhenNoModelMatch()
    {
        var costOptions = new CostTrackingOptions
        {
            Enabled = true,
            DefaultCostRate = new ModelCostRate
            {
                InputCostPer1MTokens = 1.0m,
                OutputCostPer1MTokens = 2.0m
            }
        };
        var calc = CreateCalculator(costOptions);

        // (500 * 1.0 + 500 * 2.0) / 1_000_000 = 1500 / 1_000_000 = 0.0015
        var cost = calc.CalculateCost("SomeProvider", "some-model", 500, 500);

        cost.ShouldNotBeNull();
        cost!.Value.ShouldBe(0.0015m);
    }

    [Fact]
    public void CalculateCost_NoMatchAndNoDefault_ReturnsNull()
    {
        var costOptions = new CostTrackingOptions
        {
            Enabled = true,
            DefaultCostRate = null
        };
        var calc = CreateCalculator(costOptions);

        var cost = calc.CalculateCost("UnknownProvider", "unknown-model", 1000, 500);

        cost.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Zero tokens
    // -------------------------------------------------------------------------

    [Fact]
    public void CalculateCost_ZeroTokens_ReturnsZero()
    {
        var costOptions = new CostTrackingOptions
        {
            Enabled = true,
            ModelCosts = new(StringComparer.OrdinalIgnoreCase)
            {
                ["OpenAI"] = new()
                {
                    ["gpt-4o"] = new ModelCostRate
                    {
                        InputCostPer1MTokens = 5.0m,
                        OutputCostPer1MTokens = 15.0m
                    }
                }
            }
        };
        var calc = CreateCalculator(costOptions);

        var cost = calc.CalculateCost("OpenAI", "gpt-4o", 0, 0);

        cost.ShouldNotBeNull();
        cost!.Value.ShouldBe(0m);
    }

    // -------------------------------------------------------------------------
    // Precision and rounding
    // -------------------------------------------------------------------------

    [Fact]
    public void CalculateCost_RoundsToSixDecimalPlaces()
    {
        var costOptions = new CostTrackingOptions
        {
            Enabled = true,
            ModelCosts = new(StringComparer.OrdinalIgnoreCase)
            {
                ["OpenAI"] = new()
                {
                    ["gpt-4o"] = new ModelCostRate
                    {
                        InputCostPer1MTokens = 5.0m,
                        OutputCostPer1MTokens = 15.0m
                    }
                }
            }
        };
        var calc = CreateCalculator(costOptions);

        // (1 * 5.0 + 1 * 15.0) / 1_000_000 = 20 / 1_000_000 = 0.00002
        var cost = calc.CalculateCost("OpenAI", "gpt-4o", 1, 1);

        cost.ShouldNotBeNull();
        // Verify it fits in 6 decimal places
        var roundedBack = Math.Round(cost!.Value, 6);
        roundedBack.ShouldBe(cost.Value);
    }

    [Fact]
    public void CalculateCost_LargeTokenCount_ComputesCorrectly()
    {
        var costOptions = new CostTrackingOptions
        {
            Enabled = true,
            ModelCosts = new(StringComparer.OrdinalIgnoreCase)
            {
                ["OpenAI"] = new()
                {
                    ["gpt-4o"] = new ModelCostRate
                    {
                        InputCostPer1MTokens = 5.0m,
                        OutputCostPer1MTokens = 15.0m
                    }
                }
            }
        };
        var calc = CreateCalculator(costOptions);

        // 1,000,000 input + 1,000,000 output = (5.0 + 15.0) = $20
        var cost = calc.CalculateCost("OpenAI", "gpt-4o", 1_000_000, 1_000_000);

        cost.ShouldNotBeNull();
        cost!.Value.ShouldBe(20.0m);
    }

    // -------------------------------------------------------------------------
    // Provider/model boundary values
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Anthropic", "claude-3-5-sonnet", 100, 50)]
    [InlineData("OpenAI", "gpt-4.1-mini", 200, 100)]
    public void CalculateCost_DifferentProviders_ComputeBasedOnMatchedRate(
        string provider, string model, int inputTokens, int outputTokens)
    {
        var costOptions = new CostTrackingOptions
        {
            Enabled = true,
            DefaultCostRate = new ModelCostRate
            {
                InputCostPer1MTokens = 1.0m,
                OutputCostPer1MTokens = 2.0m
            }
        };
        var calc = CreateCalculator(costOptions);

        var cost = calc.CalculateCost(provider, model, inputTokens, outputTokens);

        cost.ShouldNotBeNull();
        var expected = Math.Round((inputTokens * 1.0m + outputTokens * 2.0m) / 1_000_000m, 6);
        cost!.Value.ShouldBe(expected);
    }
}
