using System;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Osm.Cli.Commands.Options;

internal sealed class OpenReportVerbExtension : IVerbOptionExtension
{
    private readonly Option<bool> _openReportOption = new("--open-report", "Generate and open an HTML report for this run.");

    public OpenReportVerbExtension(string verbName)
    {
        if (string.IsNullOrWhiteSpace(verbName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(verbName));
        }

        VerbName = verbName;
    }

    public string VerbName { get; }

    public Type ResultType => typeof(OpenReportSettings);

    public void Configure(IVerbOptionsBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.AddOption(_openReportOption);
    }

    public object Bind(ParseResult parseResult)
    {
        if (parseResult is null)
        {
            throw new ArgumentNullException(nameof(parseResult));
        }

        return new OpenReportSettings(parseResult.GetValueForOption(_openReportOption));
    }
}

internal sealed record OpenReportSettings(bool OpenReport);
