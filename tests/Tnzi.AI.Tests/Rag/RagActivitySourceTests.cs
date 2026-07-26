using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tnzi.AI.Rag.Observability;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// RagActivitySource 单元测试 - 验证 ActivitySource/Meter 名称、Activity 创建和指标记录
/// </summary>
public class RagActivitySourceTests
{
    [Fact]
    public void Name_IsCorrect()
    {
        RagActivitySource.Name.ShouldBe("Tnzi.AI.Rag");
    }

    [Fact]
    public void Version_IsCorrect()
    {
        RagActivitySource.Version.ShouldBe("1.0.0");
    }

    [Fact]
    public void Source_IsNotNull()
    {
        RagActivitySource.Source.ShouldNotBeNull();
        RagActivitySource.Source.Name.ShouldBe("Tnzi.AI.Rag");
        RagActivitySource.Source.Version.ShouldBe("1.0.0");
    }

    [Fact]
    public void Meter_IsNotNull()
    {
        RagActivitySource.Meter.ShouldNotBeNull();
        RagActivitySource.Meter.Name.ShouldBe("Tnzi.AI.Rag");
        RagActivitySource.Meter.Version.ShouldBe("1.0.0");
    }

    [Fact]
    public void StartIngestionActivity_SetsTagsCorrectly()
    {
        // 注册监听器来捕获 Activity
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RagActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var kbId = Guid.NewGuid();
        var activity = RagActivitySource.StartIngestionActivity(kbId, "document.pdf");

        if (activity != null)
        {
            activity.OperationName.ShouldBe("rag.ingest");
            activity.Kind.ShouldBe(ActivityKind.Internal);
            activity.GetTagItem("rag.knowledge_base.id").ShouldBe(kbId.ToString());
            activity.GetTagItem("rag.document.file_name").ShouldBe("document.pdf");
            activity.Dispose();
        }
    }

    [Fact]
    public void StartSearchActivity_SetsTagsCorrectly()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RagActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var kbId = Guid.NewGuid();
        var activity = RagActivitySource.StartSearchActivity(topK: 10, knowledgeBaseId: kbId);

        if (activity != null)
        {
            activity.OperationName.ShouldBe("rag.search");
            activity.Kind.ShouldBe(ActivityKind.Internal);
            activity.GetTagItem("rag.search.top_k").ShouldBe(10);
            activity.GetTagItem("rag.knowledge_base.id").ShouldBe(kbId.ToString());
            activity.Dispose();
        }
    }

    [Fact]
    public void StartSearchActivity_WithoutKnowledgeBaseId_NoKbTag()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RagActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var activity = RagActivitySource.StartSearchActivity(topK: 5);

        if (activity != null)
        {
            activity.GetTagItem("rag.search.top_k").ShouldBe(5);
            activity.GetTagItem("rag.knowledge_base.id").ShouldBeNull();
            activity.Dispose();
        }
    }

    [Fact]
    public void RecordIngestion_DoesNotThrow()
    {
        // 验证 RecordIngestion 不会抛异常（即使没有 MeterListener）
        Should.NotThrow(() => RagActivitySource.RecordIngestion(Guid.NewGuid(), chunkCount: 10, durationSeconds: 1.5));
    }

    [Fact]
    public void RecordSearch_DoesNotThrow()
    {
        Should.NotThrow(() => RagActivitySource.RecordSearch(resultCount: 5, durationSeconds: 0.3));
        Should.NotThrow(() => RagActivitySource.RecordSearch(resultCount: 5, durationSeconds: 0.3, knowledgeBaseId: Guid.NewGuid()));
    }

    [Fact]
    public void RecordError_DoesNotThrow()
    {
        Should.NotThrow(() => RagActivitySource.RecordError("ingestion"));
        Should.NotThrow(() => RagActivitySource.RecordError("search", new InvalidOperationException("test error")));
    }

    [Fact]
    public void RecordEmbeddingCacheHitAndMiss_DoNotThrow()
    {
        Should.NotThrow(() => RagActivitySource.RecordEmbeddingCacheHit());
        Should.NotThrow(() => RagActivitySource.RecordEmbeddingCacheMiss());
    }

    [Fact]
    public void RecordActivityError_NullActivity_DoesNotThrow()
    {
        // 当 Activity 为 null 时应直接返回
        Should.NotThrow(() => RagActivitySource.RecordActivityError(null, new Exception("test")));
    }

    [Fact]
    public void RecordActivityError_SetsErrorStatus()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RagActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var activity = RagActivitySource.StartIngestionActivity(Guid.NewGuid(), "test.pdf");

        if (activity != null)
        {
            var exception = new InvalidOperationException("Test error");
            RagActivitySource.RecordActivityError(activity, exception);

            activity.Status.ShouldBe(ActivityStatusCode.Error);
            activity.StatusDescription.ShouldBe("Test error");
            activity.GetTagItem("error.type").ShouldBe(typeof(InvalidOperationException).FullName);

            // 验证 exception 事件
            activity.Events.ShouldContain(e => e.Name == "exception");

            activity.Dispose();
        }
    }

    [Fact]
    public void RecordIngestion_WithMeterListener_RecordsMetrics()
    {
        // 使用 MeterListener 捕获指标
        var ingestionCount = 0L;
        var chunkCount = 0L;

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == RagActivitySource.Name)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "rag.ingestion.count") ingestionCount += measurement;
            else if (instrument.Name == "rag.ingestion.chunk.count") chunkCount += measurement;
        });

        meterListener.Start();

        RagActivitySource.RecordIngestion(Guid.NewGuid(), chunkCount: 15, durationSeconds: 2.0);

        meterListener.RecordObservableInstruments();

        ingestionCount.ShouldBe(1);
        chunkCount.ShouldBe(15);
    }
}
