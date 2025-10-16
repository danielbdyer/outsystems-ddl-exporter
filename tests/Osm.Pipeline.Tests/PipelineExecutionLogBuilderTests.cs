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
