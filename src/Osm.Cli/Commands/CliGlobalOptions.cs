using System.CommandLine;

namespace Osm.Cli.Commands;

internal sealed class CliGlobalOptions
{
    public CliGlobalOptions()
    {
        ConfigPath = new Option<string?>("--config", "Path to CLI configuration file.");
        MaxDegreeOfParallelism = new Option<int?>("--max-degree-of-parallelism", "Maximum number of modules processed in parallel.");
    }

    public Option<string?> ConfigPath { get; }

    public Option<int?> MaxDegreeOfParallelism { get; }
}
