using System.Collections.Generic;
using System.CommandLine;

namespace Osm.Cli.Commands.Binders;

internal interface ICommandOptionSource
{
    IEnumerable<Option> Options { get; }
}
