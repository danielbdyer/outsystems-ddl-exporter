using System;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Osm.Cli.Commands.Options;

internal interface IVerbOptionExtension
{
    string VerbName { get; }

    Type ResultType { get; }

    void Configure(IVerbOptionsBuilder builder);

    object? Bind(ParseResult parseResult);
}

internal interface IVerbOptionsBuilder
{
    void AddOption(Option option);
}
