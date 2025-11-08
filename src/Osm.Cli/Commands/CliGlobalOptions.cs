using System.CommandLine;

namespace Osm.Cli.Commands;

internal enum CliVerbosity
{
    Normal,
    Verbose
}

internal sealed class CliGlobalOptions
{
    public CliGlobalOptions()
    {
        ConfigPath = new Option<string?>("--config", "Path to CLI configuration file.");
        MaxDegreeOfParallelism = new Option<int?>("--max-degree-of-parallelism", "Maximum number of modules processed in parallel.");
        Verbosity = new Option<CliVerbosity>("--verbosity", () => CliVerbosity.Normal, "Controls the amount of diagnostic output emitted by the CLI.");
    }

    public Option<string?> ConfigPath { get; }

    public Option<int?> MaxDegreeOfParallelism { get; }

    public Option<CliVerbosity> Verbosity { get; }
}
