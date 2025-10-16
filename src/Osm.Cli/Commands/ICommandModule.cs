using System.CommandLine;

namespace Osm.Cli.Commands;

internal interface ICommandModule
{
    Command BuildCommand();
}
