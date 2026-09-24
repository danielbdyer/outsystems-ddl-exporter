using System.CommandLine;

namespace Osm.Cli.Commands;

internal interface ICommandFactory
{
    Command Create();
}
