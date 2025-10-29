using System;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Tests;

public sealed class PipelineExecutionLogBuilderTests
{
    [Fact]
    public void Record_UsesInjectedTimeProvider()
    {
        var first = new DateTimeOffset(2024, 01, 02, 03, 04, 05, TimeSpan.Zero);
        var provider = new StubTimeProvider(first);
        var builder = new PipelineExecutionLogBuilder(provider);

        builder.Record("step", "message");
        var log = builder.Build();

        Assert.Single(log.Entries);
        Assert.Equal(first, log.Entries[0].TimestampUtc);

        var second = first.AddMinutes(1);
        provider.SetUtcNow(second);
        builder.Record("step2", "message2");
        log = builder.Build();

        Assert.Equal(2, log.Entries.Count);
        Assert.Equal(second, log.Entries[^1].TimestampUtc);
    }

    [Fact]
    public void MetadataBuilder_ProducesCategorizedKeys()
    {
        var timestamp = new DateTimeOffset(2024, 02, 03, 04, 05, 06, TimeSpan.Zero);

        var metadata = new PipelineLogMetadataBuilder()
            .WithCount("items", 3)
            .WithFlag("feature.enabled", true)
            .WithPath("output", "/tmp/output")
            .WithTimestamp("event.start", timestamp)
            .WithValue("custom.note", "value")
            .Build();

        Assert.Equal("3", metadata["counts.items"]);
        Assert.Equal("true", metadata["flags.feature.enabled"]);
        Assert.Equal("/tmp/output", metadata["paths.output"]);
        Assert.Equal(timestamp.ToString("O"), metadata["timestamps.event.start"]);
        Assert.Equal("value", metadata["custom.note"]);
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public StubTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
