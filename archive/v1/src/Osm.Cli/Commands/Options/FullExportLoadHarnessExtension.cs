using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands.Options;

internal sealed class FullExportLoadHarnessExtension : IVerbOptionExtension
{
    private readonly Option<bool> _runHarnessOption = new("--run-load-harness", () => false, "Replay generated scripts against a staging database and capture telemetry.");
    private readonly Option<string?> _connectionStringOption = new("--load-harness-connection-string", "Connection string override for the load harness (defaults to apply connection string).");
    private readonly Option<string?> _reportOption = new("--load-harness-report-out", "Path to write the load harness telemetry report (JSON).");
    private readonly Option<int?> _commandTimeoutOption = new("--load-harness-command-timeout", "Command timeout in seconds for load harness batches (defaults to apply timeout).");

    public string VerbName => FullExportVerb.VerbName;

    public Type ResultType => typeof(LoadHarnessCliOptions);

    public void Configure(IVerbOptionsBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.AddOption(_runHarnessOption);
        builder.AddOption(_connectionStringOption);
        builder.AddOption(_reportOption);
        builder.AddOption(_commandTimeoutOption);
    }

    public object Bind(ParseResult parseResult)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        return new LoadHarnessCliOptions(
            parseResult.GetValueForOption(_runHarnessOption),
            parseResult.GetValueForOption(_connectionStringOption),
            parseResult.GetValueForOption(_reportOption),
            parseResult.GetValueForOption(_commandTimeoutOption));
    }
}

internal sealed record LoadHarnessCliOptions(
    bool RunHarness,
    string? ConnectionString,
    string? ReportOutputPath,
    int? CommandTimeoutSeconds)
{
    public static LoadHarnessCliOptions Disabled { get; } = new(false, null, null, null);
}
