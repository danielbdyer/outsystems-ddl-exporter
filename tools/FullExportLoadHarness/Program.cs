using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Osm.LoadHarness;

var connectionOption = new Option<string>(
    name: "--connection-string",
    description: "Connection string for the staging SQL database.")
{ IsRequired = true };

var safeOption = new Option<string?>("--safe-script", "Path to the generated safe script.");
var remediationOption = new Option<string?>("--remediation-script", "Path to the remediation script (optional).");
var staticSeedOption = new Option<string[]>(
    name: "--static-seed",
    description: "Paths to static seed scripts to replay.")
{ AllowMultipleArgumentsPerToken = true };

var dynamicInsertOption = new Option<string[]>(
    name: "--dynamic-insert",
    description: "Paths to dynamic insert scripts to replay.")
{ AllowMultipleArgumentsPerToken = true };

var reportOption = new Option<string?>("--report-out", () => "load-harness.report.json", "Output path for the JSON report.");
var timeoutOption = new Option<int?>("--command-timeout", "Command timeout (seconds) for batch execution.");

var rootCommand = new RootCommand("Replay full-export scripts against a staging database and capture performance telemetry")
{
    connectionOption,
    safeOption,
    remediationOption,
    staticSeedOption,
    dynamicInsertOption,
    reportOption,
    timeoutOption
};

rootCommand.SetHandler(async context =>
{
    var connection = context.ParseResult.GetValueForOption(connectionOption)!;
    var safe = context.ParseResult.GetValueForOption(safeOption);
    var remediation = context.ParseResult.GetValueForOption(remediationOption);
    var staticSeeds = context.ParseResult.GetValueForOption(staticSeedOption) ?? Array.Empty<string>();
    var dynamicInserts = context.ParseResult.GetValueForOption(dynamicInsertOption) ?? Array.Empty<string>();
    var reportOut = context.ParseResult.GetValueForOption(reportOption);
    var timeout = context.ParseResult.GetValueForOption(timeoutOption);

    using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddSimpleConsole(o =>
        {
            o.ColorBehavior = LoggerColorBehavior.Enabled;
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
        builder.SetMinimumLevel(LogLevel.Information);
    });

    var fileSystem = new FileSystem();
    var options = LoadHarnessOptions.Create(connection, safe, remediation, staticSeeds, dynamicInserts, reportOut, timeout);

    var runner = new LoadHarnessRunner(fileSystem, TimeProvider.System, loggerFactory.CreateLogger<LoadHarnessRunner>());
    var report = await runner.RunAsync(options, context.GetCancellationToken()).ConfigureAwait(false);

    var writer = new LoadHarnessReportWriter(fileSystem);
    await writer.WriteAsync(report, fileSystem.Path.GetFullPath(options.ReportOutputPath), context.GetCancellationToken())
        .ConfigureAwait(false);

    context.Console.Out.Write($"Load harness completed in {report.TotalDuration:g}. Report written to {options.ReportOutputPath}.{Environment.NewLine}");
});

return await rootCommand.InvokeAsync(args);
