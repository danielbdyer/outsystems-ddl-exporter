module Projection.Cli.VerbArgs

open System
open Argu

/// The canonical Argu-verb dispatch primitive. Every CLI verb that
/// parses its arguments via Argu shares this shape: instantiate the
/// parser with the verb's program name, parse with `ProcessExiter` (so
/// `--help` and Argu's own error narration land via the conventional
/// channel), check `IsUsageRequested` for the explicit-zero path, and
/// catch `ArguParseException` so an in-process malformed argument
/// doesn't escape as an unhandled exception. Two consumers
/// (`dispatchFullExport`, `dispatchTransfer`) share this exact
/// boilerplate; the primitive removes the duplication and gives the
/// next CLI verb a fixed insertion point.
let parse<'arg when 'arg :> IArgParserTemplate>
    (programName: string)
    (run: ParseResults<'arg> -> int)
    (argv: string[])
    : int =
    let parser =
        ArgumentParser.Create<'arg>(
            programName = programName,
            errorHandler = ProcessExiter())
    try
        let parsed = parser.Parse(argv, raiseOnUsage = false)
        if parsed.IsUsageRequested then 0
        else run parsed
    with
    | :? ArguParseException as ex ->
        Console.Error.WriteLine ex.Message
        1
