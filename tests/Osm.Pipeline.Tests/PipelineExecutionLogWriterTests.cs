using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Orchestration;
using Tests.Support;

namespace Osm.Pipeline.Tests;

public sealed class PipelineExecutionLogWriterTests
{
    [Fact]
    public async Task WriteAsync_SerializesLogEntries()
    {
        using var output = new TempDirectory();
        var first = new DateTimeOffset(2024, 01, 02, 03, 04, 05, TimeSpan.Zero);
        var provider = new StubTimeProvider(first);
        var builder = new PipelineExecutionLogBuilder(provider);

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["id"] = "abc",
            ["notes"] = null
        };

        builder.Record("bootstrap", "Started bootstrap.", metadata);
        provider.SetUtcNow(first.AddMinutes(5));
        builder.Record("bootstrap", "Completed bootstrap.");

        var log = builder.Build();
        var writer = new PipelineExecutionLogWriter();

        var path = await writer.WriteAsync(output.Path, log, CancellationToken.None);

        Assert.Equal(Path.Combine(output.Path, "pipeline-log.json"), path);
        Assert.True(File.Exists(path));

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var entries = document.RootElement.GetProperty("Entries");

        Assert.Equal(2, entries.GetArrayLength());

        var firstEntry = entries[0];
        Assert.Equal(first.ToString("O"), firstEntry.GetProperty("TimestampUtc").GetString());
        Assert.Equal("bootstrap", firstEntry.GetProperty("Step").GetString());
        Assert.Equal("Started bootstrap.", firstEntry.GetProperty("Message").GetString());
        var firstMetadata = firstEntry.GetProperty("Metadata");
        Assert.Equal("abc", firstMetadata.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, firstMetadata.GetProperty("notes").ValueKind);

        var secondEntry = entries[1];
        Assert.Equal("Completed bootstrap.", secondEntry.GetProperty("Message").GetString());
        Assert.Empty(secondEntry.GetProperty("Metadata").EnumerateObject());
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
