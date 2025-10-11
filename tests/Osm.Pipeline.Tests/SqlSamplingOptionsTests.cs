using System;
using Osm.Pipeline.Sql;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class SqlSamplingOptionsTests
{
    [Fact]
    public void Default_ShouldExposeExpectedValues()
    {
        var options = SqlSamplingOptions.Default;

        Assert.Equal(250_000, options.RowCountSamplingThreshold);
        Assert.Equal(50_000, options.SampleSize);
    }

    [Fact]
    public void Create_ShouldReturnOptions_WhenValuesValid()
    {
        var options = SqlSamplingOptions.Create(threshold: 100_000, sampleSize: 10_000);

        Assert.Equal(100_000, options.RowCountSamplingThreshold);
        Assert.Equal(10_000, options.SampleSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ShouldThrow_WhenThresholdNotPositive(long threshold)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => SqlSamplingOptions.Create(threshold, sampleSize: 10_000));
        Assert.Equal("threshold", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_ShouldThrow_WhenSampleSizeNotPositive(int sampleSize)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => SqlSamplingOptions.Create(threshold: 10_000, sampleSize));
        Assert.Equal("sampleSize", exception.ParamName);
    }

    [Fact]
    public void SqlProfilerOptions_Default_ShouldUseDefaultSampling()
    {
        var options = SqlProfilerOptions.Default;

        Assert.Null(options.CommandTimeoutSeconds);
        Assert.Equal(SqlSamplingOptions.Default, options.Sampling);
        Assert.Equal(4, options.MaxDegreeOfParallelism);
        Assert.Equal(32, options.TablesPerBatch);
        Assert.Equal(SqlRetryPolicyOptions.Default, options.RetryPolicy);
    }
}
